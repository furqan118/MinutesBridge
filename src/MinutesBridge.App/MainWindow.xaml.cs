using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using Microsoft.Identity.Client;
using Microsoft.Win32;
using MinutesBridge.App.Editing;
using MinutesBridge.App.Security;
using MinutesBridge.App.Teams;
using MinutesBridge.Core.Authentication;
using MinutesBridge.Core.Confluence;
using MinutesBridge.Core.Editing;
using MinutesBridge.Core.Models;
using MinutesBridge.Core.Parsing;
using MinutesBridge.Core.Teams;

namespace MinutesBridge.App;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WPF window resources are disposed by the existing Closed event handler.")]
public partial class MainWindow : Window
{
    private const int MaximumPeople = 250;
    private const int MaximumClipboardPayloadCharacters = 1_000_000;
    private readonly FacilitatorNotesParser _parser = new();
    private readonly ObservableCollection<EditableAgendaRow> _agendaRows = [];
    private readonly ConfluenceStorageRenderer _renderer = new();
    private readonly HttpClient _httpClient = CreateHttpClient();
    private readonly WindowsCredentialManagerSessionStore _sessionStore = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _destinationLoad;
    private MeetingMinutes? _current;
    private BrokerSession? _session;
    private ConfluenceCatalogClient? _catalog;
    private ConfluencePageSummary? _previousMinutes;
    private string? _previousMinutesGroup;
    private DateOnly? _previousMinutesBeforeDate;
    private bool _loadingSpaces;
    private bool _agendaNeedsRebuild;
    private MicrosoftTeamsAuthentication? _teamsAuthentication;
    private TeamsGraphClient? _teamsClient;
    private string? _teamsAccessToken;

    public MainWindow()
    {
        InitializeComponent();
        DateInput.SelectedDate = DateTime.Today;
        AgendaGrid.DataContext = _agendaRows;
        BuildAgendaFromNotes();
        NotesInput.TextChanged += NotesInput_TextChanged;
        Loaded += Window_Loaded;
        Closed += Window_Closed;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var saved = _sessionStore.TryLoad();
            if (saved is not null)
            {
                await UseSessionAsync(saved, _lifetime.Token);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or HttpRequestException or IOException or TimeoutException or
                                   System.ComponentModel.Win32Exception)
        {
            SetConnectionError();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _lifetime.Cancel();
        _destinationLoad?.Cancel();
        _destinationLoad?.Dispose();
        _lifetime.Dispose();
        _httpClient.Dispose();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        ConnectionStatus.Text = "Waiting for browser sign-in…";

        try
        {
            var broker = new OAuthBrokerClient(
                _httpClient,
                MinutesBridge.Core.Authentication.BrokerOptions.FromEnvironment());
            var start = await broker.StartAsync(_lifetime.Token);
            Process.Start(new ProcessStartInfo(start.AuthorizationUri.AbsoluteUri) { UseShellExecute = true });

            while (DateTimeOffset.UtcNow < start.ExpiresAtUtc)
            {
                await Task.Delay(start.PollInterval, _lifetime.Token);
                var result = await broker.PollAsync(start.RequestId, start.PollingSecret, _lifetime.Token);
                if (result.Status == BrokerAuthorizationStatus.Pending)
                {
                    continue;
                }

                if (result.Status != BrokerAuthorizationStatus.Authorized || result.Session is null)
                {
                    throw new InvalidOperationException("Confluence authorization was not completed.");
                }

                _sessionStore.Save(result.Session);
                await UseSessionAsync(result.Session, _lifetime.Token);
                return;
            }

            throw new TimeoutException("Confluence authorization expired.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window is closing; no user notification is required.
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or HttpRequestException or
                                   IOException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            SetConnectionError();
            MessageBox.Show(
                "MinutesBridge could not establish a secure Confluence session. Check the broker configuration or contact your administrator.",
                "Confluence connection failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private async void ConnectTeams_Click(object sender, RoutedEventArgs e)
    {
        TeamsConnectButton.IsEnabled = false;
        CheckTeamsButton.IsEnabled = false;
        TeamsConnectionStatus.Text = "Waiting for Microsoft sign-in…";

        try
        {
            _teamsAuthentication ??= new MicrosoftTeamsAuthentication();
            var result = await _teamsAuthentication.AcquireAsync(
                new WindowInteropHelper(this).Handle,
                _lifetime.Token);
            _teamsAccessToken = result.AccessToken;
            _teamsClient = new TeamsGraphClient(_httpClient);
            var chats = await _teamsClient.GetMeetingChatsAsync(result.AccessToken, _lifetime.Token);
            TeamsChatInput.ItemsSource = chats;
            TeamsChatInput.IsEnabled = chats.Count > 0;
            CheckTeamsButton.IsEnabled = chats.Count > 0;
            TeamsConnectionStatus.Text = chats.Count == 0
                ? "Connected, but no accessible meeting chats were returned"
                : $"Connected • {chats.Count} meeting chat{(chats.Count == 1 ? string.Empty : "s")} available";

            if (chats.Count > 0)
            {
                var preferred = chats.FirstOrDefault(chat =>
                    chat.Topic.Contains("BHITS", StringComparison.OrdinalIgnoreCase));
                TeamsChatInput.SelectedItem = preferred ?? chats[0];
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window is closing; no user notification is required.
        }
        catch (Exception ex) when (ex is MsalException or InvalidOperationException or ArgumentException or
                                   HttpRequestException or IOException or TimeoutException)
        {
            ResetTeamsConnection();
            MessageBox.Show(
                "MinutesBridge could not connect to Microsoft Teams. Confirm the Entra application configuration and delegated Chat.Read access, then try again.",
                "Teams connection failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            TeamsConnectButton.IsEnabled = true;
        }
    }

    private async void CheckTeamsNow_Click(object sender, RoutedEventArgs e)
    {
        if (_teamsClient is null ||
            string.IsNullOrWhiteSpace(_teamsAccessToken) ||
            TeamsChatInput.SelectedItem is not TeamsMeetingChat chat)
        {
            return;
        }

        CheckTeamsButton.IsEnabled = false;
        TeamsConnectionStatus.Text = "Checking the selected meeting chat…";
        try
        {
            var messages = await _teamsClient.GetRecentMessagesAsync(
                _teamsAccessToken,
                chat.Id,
                _lifetime.Token);
            var imported = TeamsSummaryExtractor.Extract(chat.Id, messages);
            GroupInput.Text = "BHITS";
            DateInput.SelectedDate = imported.MeetingDate.ToDateTime(TimeOnly.MinValue);
            NotesInput.Text = imported.Content;
            BuildAgendaFromNotes();
            TeamsConnectionStatus.Text = "Teams rundown imported • Review required";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window is closing; no user notification is required.
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or HttpRequestException or TimeoutException)
        {
            TeamsConnectionStatus.Text = "No import was made";
            MessageBox.Show(
                ex is InvalidDataException
                    ? ex.Message
                    : "MinutesBridge could not read the selected Teams meeting chat.",
                "Teams import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            CheckTeamsButton.IsEnabled = TeamsChatInput.SelectedItem is TeamsMeetingChat;
        }
    }

    private async Task UseSessionAsync(BrokerSession session, CancellationToken cancellationToken)
    {
        _session = session;
        _catalog = new ConfluenceCatalogClient(_httpClient, session.CloudId);
        ConnectionStatus.Text = string.IsNullOrWhiteSpace(session.AccountDisplayName)
            ? "Connected"
            : $"Connected as {session.AccountDisplayName}";
        ConnectButton.Content = "Reconnect";
        await LoadSpacesAsync(cancellationToken);
    }

    private async Task LoadSpacesAsync(CancellationToken cancellationToken)
    {
        if (_session is null || _catalog is null)
        {
            return;
        }

        _loadingSpaces = true;
        _previousMinutes = null;
        _previousMinutesGroup = null;
        _previousMinutesBeforeDate = null;
        try
        {
            var spaces = await _catalog.GetSpacesAsync(_session.AccessToken, cancellationToken);
            SpaceInput.ItemsSource = spaces;
            SpaceInput.IsEnabled = spaces.Count > 0;
            ConnectionStatus.Text = spaces.Count == 0
                ? "Connected, but no authorized spaces were returned"
                : ConnectionStatus.Text;
            if (spaces.Count > 0)
            {
                SpaceInput.SelectedIndex = 0;
            }
        }
        finally
        {
            _loadingSpaces = false;
        }

        if (SpaceInput.SelectedItem is ConfluenceSpace selected)
        {
            await RefreshDestinationAsync(selected);
        }
    }

    private async void SpaceInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSpaces || SpaceInput.SelectedItem is not ConfluenceSpace selected)
        {
            return;
        }

        try
        {
            await RefreshDestinationAsync(selected);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or ArgumentException or TimeoutException)
        {
            _previousMinutes = null;
            _previousMinutesGroup = null;
            _previousMinutesBeforeDate = null;
            ParentPageInput.ItemsSource = null;
            ParentPageInput.IsEnabled = false;
            PreviousMinutesStatus.Text = "Pages could not be loaded.";
        }
    }

    private async Task LoadPagesAsync(ConfluenceSpace space, CancellationToken cancellationToken)
    {
        if (_session is null || _catalog is null)
        {
            return;
        }

        ParentPageInput.IsEnabled = false;
        ParentPageInput.ItemsSource = null;
        _previousMinutes = null;
        _previousMinutesGroup = null;
        _previousMinutesBeforeDate = null;
        PreviousMinutesStatus.Text = "Loading authorized pages…";
        var pages = await _catalog.GetRootPagesAsync(
            _session.AccessToken,
            space.Id,
            _session.SiteUri,
            cancellationToken);
        ParentPageInput.ItemsSource = pages;
        ParentPageInput.IsEnabled = pages.Count > 0;
        if (pages.Count > 0)
        {
            ParentPageInput.SelectedIndex = 0;
        }

        var group = string.IsNullOrWhiteSpace(GroupInput.Text) ? "BHITS" : GroupInput.Text.Trim();
        var beforeDate = DateInput.SelectedDate is DateTime selectedDate
            ? DateOnly.FromDateTime(selectedDate)
            : DateOnly.FromDateTime(DateTime.Today);
        _previousMinutes = await _catalog.FindLatestMeetingPageAsync(
            _session.AccessToken,
            space.Id,
            group,
            beforeDate,
            _session.SiteUri,
            cancellationToken);
        _previousMinutesGroup = group;
        _previousMinutesBeforeDate = beforeDate;
        PreviousMinutesStatus.Text = _previousMinutes is null
            ? $"Previous {group} page: none found in the first 100 recent pages"
            : $"Previous {group} page: {_previousMinutes.Title}";
    }

    private async Task RefreshDestinationAsync(ConfluenceSpace space)
    {
        var next = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var previous = Interlocked.Exchange(ref _destinationLoad, next);
        previous?.Cancel();
        previous?.Dispose();
        try
        {
            await LoadPagesAsync(space, next.Token);
        }
        catch (OperationCanceledException) when (next.IsCancellationRequested)
        {
            // A newer selection or application shutdown replaced this request.
        }
        catch (OperationCanceledException ex)
        {
            throw new TimeoutException("Confluence destination discovery timed out.", ex);
        }
    }

    private void GeneratePreview_Click(object sender, RoutedEventArgs e)
    {
        _current = null;
        try
        {
            _current = ReadMeeting();
            TitlePreview.Text = _current.PageTitle;
            PreviewOutput.Text = ToReadablePreview(_current);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "Check meeting details", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveDraft_Click(object sender, RoutedEventArgs e)
    {
        GeneratePreview_Click(sender, e);
        if (_current is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = $"{_current.Date:yyyy-MM-dd}-{SafeFileName(_current.Group)}-meeting-notes.html",
            Filter = "HTML file (*.html)|*.html",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllText(dialog.FileName, _renderer.Render(_current), Encoding.UTF8);
            MessageBox.Show("The local draft was saved.", "MinutesBridge", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Publish_Click(object sender, RoutedEventArgs e)
    {
        GeneratePreview_Click(sender, e);
        if (_current is null)
        {
            return;
        }

        var destinationReady = _session is not null &&
                               SpaceInput.SelectedItem is ConfluenceSpace &&
                               ParentPageInput.SelectedItem is ConfluencePageSummary;
        MessageBox.Show(
            destinationReady
                ? "Authentication and destination discovery are ready. Restricted-draft publishing is intentionally gated until the next reviewed milestone; no content was published."
                : "Connect to Confluence and select an authorized space and parent page. No content was published.",
            "Publishing remains gated",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private MeetingMinutes ReadMeeting()
    {
        if (_agendaNeedsRebuild)
        {
            throw new ArgumentException("Meeting notes changed. Rebuild the editable agenda before generating the preview.");
        }

        AgendaGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        AgendaGrid.CommitEdit(DataGridEditingUnit.Row, true);

        if (DateInput.SelectedDate is not DateTime selectedDate)
        {
            throw new ArgumentException("Choose the meeting date.");
        }

        var group = RequiredBounded(GroupInput.Text, "meeting group", 100);
        var attendees = Lines(AttendeesInput.Text, "attendees");
        var regrets = Lines(RegretsInput.Text, "regrets");

        return new MeetingMinutes(
            group,
            DateOnly.FromDateTime(selectedDate),
            Bounded(TimeInput.Text, "time", 100),
            "Microsoft TEAMS",
            Bounded(FacilitatorInput.Text, "facilitator", 200),
            Bounded(NoteTakerInput.Text, "note taker", 200),
            attendees,
            regrets,
            AgendaDraftValidator.ValidateAndConvert(_agendaRows.Select(row => row.ToDraft())),
            string.Equals(group, _previousMinutesGroup, StringComparison.OrdinalIgnoreCase) &&
            DateOnly.FromDateTime(selectedDate) == _previousMinutesBeforeDate
                ? _previousMinutes?.WebUri
                : null);
    }

    private void PasteClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = ReadClipboardText();
            if (text.Length > FacilitatorNotesParser.MaximumInputCharacters)
            {
                throw new InvalidDataException(
                    $"Clipboard notes must be {FacilitatorNotesParser.MaximumInputCharacters:N0} characters or fewer.");
            }

            NotesInput.Text = text;
            BuildAgendaFromNotes();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(
                ex is ArgumentException or InvalidDataException
                    ? ex.Message
                    : "MinutesBridge could not read supported text from the clipboard.",
                "Clipboard import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BuildAgenda_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BuildAgendaFromNotes();
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "Check meeting notes", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BuildAgendaFromNotes()
    {
        var parsed = _parser.Parse(NotesInput.Text);
        _agendaRows.Clear();
        foreach (var item in parsed)
        {
            _agendaRows.Add(EditableAgendaRow.FromAgendaItem(item));
        }

        AgendaStatus.Text = $"{_agendaRows.Count} row{(_agendaRows.Count == 1 ? string.Empty : "s")} ready";
        _agendaNeedsRebuild = false;
        _current = null;
    }

    private void NotesInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        AgendaStatus.Text = "Notes changed — rebuild agenda";
        _agendaNeedsRebuild = true;
        _current = null;
    }

    private void AddAgenda_Click(object sender, RoutedEventArgs e)
    {
        if (_agendaRows.Count >= AgendaDraftValidator.MaximumRows)
        {
            MessageBox.Show(
                $"The agenda can contain at most {AgendaDraftValidator.MaximumRows} rows.",
                "Agenda limit reached",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var row = new EditableAgendaRow { Topic = "New topic" };
        _agendaRows.Add(row);
        AgendaGrid.SelectedItem = row;
        AgendaGrid.ScrollIntoView(row);
        MarkAgendaEdited();
    }

    private void RemoveAgenda_Click(object sender, RoutedEventArgs e)
    {
        if (AgendaGrid.SelectedItem is not EditableAgendaRow selected)
        {
            return;
        }

        _agendaRows.Remove(selected);
        MarkAgendaEdited();
    }

    private void MoveAgendaUp_Click(object sender, RoutedEventArgs e) => MoveSelectedAgendaRow(-1);

    private void MoveAgendaDown_Click(object sender, RoutedEventArgs e) => MoveSelectedAgendaRow(1);

    private void MoveSelectedAgendaRow(int offset)
    {
        if (AgendaGrid.SelectedItem is not EditableAgendaRow selected)
        {
            return;
        }

        var currentIndex = _agendaRows.IndexOf(selected);
        var targetIndex = currentIndex + offset;
        if (targetIndex < 0 || targetIndex >= _agendaRows.Count)
        {
            return;
        }

        _agendaRows.Move(currentIndex, targetIndex);
        AgendaGrid.SelectedItem = selected;
        MarkAgendaEdited();
    }

    private void MarkAgendaEdited()
    {
        AgendaStatus.Text = $"{_agendaRows.Count} edited row{(_agendaRows.Count == 1 ? string.Empty : "s")}";
        _current = null;
    }

    private static string ReadClipboardText()
    {
        var clipboard = Clipboard.GetDataObject() ?? throw new InvalidDataException("The clipboard is empty.");
        if (clipboard.GetDataPresent(DataFormats.Rtf) && clipboard.GetData(DataFormats.Rtf) is string rtf)
        {
            if (rtf.Length > MaximumClipboardPayloadCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(rtf), "The rich clipboard payload is too large.");
            }

            try
            {
                var document = new FlowDocument();
                var range = new TextRange(document.ContentStart, document.ContentEnd);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtf));
                range.Load(stream, DataFormats.Rtf);
                return range.Text.Trim();
            }
            catch (ArgumentException) when (clipboard.GetDataPresent(DataFormats.UnicodeText))
            {
                // Fall through to the clipboard's plain Unicode representation.
            }
        }

        if (clipboard.GetDataPresent(DataFormats.UnicodeText) && clipboard.GetData(DataFormats.UnicodeText) is string text)
        {
            if (text.Length > MaximumClipboardPayloadCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(text), "The clipboard text is too large.");
            }

            return text.Trim();
        }

        throw new InvalidDataException("The clipboard does not contain supported text.");
    }

    private static string[] Lines(string value, string fieldName)
    {
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > MaximumPeople || lines.Any(line => line.Length > 200))
        {
            throw new ArgumentOutOfRangeException(fieldName, $"{fieldName} contains too many entries or an entry is too long.");
        }

        return lines;
    }

    private static string RequiredBounded(string value, string fieldName, int maximumLength)
    {
        var result = Bounded(value, fieldName, maximumLength);
        if (result.Length == 0)
        {
            throw new ArgumentException($"Enter the {fieldName}.");
        }

        return result;
    }

    private static string Bounded(string value, string fieldName, int maximumLength)
    {
        var result = value.Trim();
        if (result.Length > maximumLength || result.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(fieldName, $"The {fieldName} is invalid or too long.");
        }

        return result;
    }

    private static string ToReadablePreview(MeetingMinutes meeting)
    {
        var text = new StringBuilder()
            .AppendLine(CultureInfo.CurrentCulture, $"Place: {meeting.Place}")
            .AppendLine(CultureInfo.CurrentCulture, $"Time: {meeting.Time}")
            .AppendLine(CultureInfo.CurrentCulture, $"Facilitator: {meeting.Facilitator}")
            .AppendLine(CultureInfo.CurrentCulture, $"Note Taker: {meeting.NoteTaker}")
            .AppendLine().AppendLine("Attendees:");

        foreach (var person in meeting.Attendees)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"  • {person}");
        }

        text.AppendLine().AppendLine("Regrets:");
        foreach (var person in meeting.Regrets)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"  • {person}");
        }

        if (meeting.PreviousMinutes is not null)
        {
            text.AppendLine().AppendLine(CultureInfo.CurrentCulture, $"Previous meeting notes: {meeting.PreviousMinutes}");
        }

        text.AppendLine().AppendLine("AGENDA/NOTE");
        foreach (var item in meeting.Agenda)
        {
            text.AppendLine().AppendLine(item.Topic);
            foreach (var note in item.Notes)
            {
                text.AppendLine(CultureInfo.CurrentCulture, $"  • {note}");
            }
        }

        return text.ToString();
    }

    private void SetConnectionError()
    {
        _session = null;
        _catalog = null;
        _previousMinutes = null;
        _previousMinutesGroup = null;
        _previousMinutesBeforeDate = null;
        SpaceInput.ItemsSource = null;
        ParentPageInput.ItemsSource = null;
        SpaceInput.IsEnabled = false;
        ParentPageInput.IsEnabled = false;
        ConnectionStatus.Text = "Not connected";
        PreviousMinutesStatus.Text = "Previous BHITS page: not loaded";
    }

    private void ResetTeamsConnection()
    {
        _teamsAccessToken = null;
        _teamsClient = null;
        TeamsChatInput.ItemsSource = null;
        TeamsChatInput.IsEnabled = false;
        CheckTeamsButton.IsEnabled = false;
        TeamsConnectionStatus.Text = "Not connected";
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private static string SafeFileName(string value) => string.Concat(
        value.Trim().Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));

    private async void DateInput_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_session is null || SpaceInput.SelectedItem is not ConfluenceSpace selected)
        {
            return;
        }

        try
        {
            await RefreshDestinationAsync(selected);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or ArgumentException or TimeoutException)
        {
            _previousMinutes = null;
            _previousMinutesGroup = null;
            _previousMinutesBeforeDate = null;
            PreviousMinutesStatus.Text = "Previous meeting page could not be refreshed.";
        }
    }
}

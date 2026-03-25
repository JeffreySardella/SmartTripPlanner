namespace SmartTripPlanner.Api.Services;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using CalendarApi = Google.Apis.Calendar.v3.CalendarService;

public static class GoogleCalendarFactory
{
    private static readonly string[] Scopes = [CalendarApi.Scope.Calendar];

    public static async Task<CalendarApi?> CreateAsync(string credentialPath, string tokenDirectory)
    {
        if (!File.Exists(credentialPath))
            return null;

        using var stream = new FileStream(credentialPath, FileMode.Open, FileAccess.Read);
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            (await GoogleClientSecrets.FromStreamAsync(stream)).Secrets,
            Scopes,
            "user",
            CancellationToken.None,
            new FileDataStore(tokenDirectory, true));

        return new CalendarApi(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "SmartTripPlanner"
        });
    }
}

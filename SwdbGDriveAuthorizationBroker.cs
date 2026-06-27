using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Util.Store;

namespace CloudStorage.Net
{ 
    public class SwdbGDriveAuthorizationBroker : GoogleWebAuthorizationBroker
    {
        public const string RedirectUri = "https://www.songwritersdb.com/auth/drivesuccess";

        public static async Task<UserCredential> AuthorizeAsync(
            ClientSecrets clientSecrets,
            IEnumerable<string> scopes,
            string user,
            CancellationToken taskCancellationToken,
            IDataStore dataStore = null)
        {
            var initializer = new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = clientSecrets,
            };
            return await AuthorizeAsyncCore(initializer, scopes, user,
                taskCancellationToken, dataStore).ConfigureAwait(false);
        }

        private static async Task<UserCredential> AuthorizeAsyncCore(
            GoogleAuthorizationCodeFlow.Initializer initializer,
            IEnumerable<string> scopes,
            string user,
            CancellationToken taskCancellationToken,
            IDataStore dataStore)
        {
            initializer.Scopes = scopes;
            initializer.DataStore = dataStore ?? new FileDataStore(Folder);
            var flow = new SwdbGDriveAuthorizationCodeFlow(initializer);
            return await new AuthorizationCodeInstalledApp(flow,
                new LocalServerCodeReceiver())
                .AuthorizeAsync(user, taskCancellationToken).ConfigureAwait(false);
        }
    }


    public class SwdbGDriveAuthorizationCodeFlow : GoogleAuthorizationCodeFlow
    {
        public SwdbGDriveAuthorizationCodeFlow(Initializer initializer)
            : base(initializer) { }

        public override AuthorizationCodeRequestUrl
                       CreateAuthorizationCodeRequest(string redirectUri)
        {
            return base.CreateAuthorizationCodeRequest(SwdbGDriveAuthorizationBroker.RedirectUri);
        }
    }
}

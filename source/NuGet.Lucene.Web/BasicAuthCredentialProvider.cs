using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Lucene.Web
{
    class BasicAuthCredentialProvider:ICredentialProvider
    {
        private string uname;
        private string pass;

        public BasicAuthCredentialProvider(string uname, string pass)
        {
            this.uname = uname;
            this.pass = pass;
        }
        public ICredentials GetCredentials(Uri uri, IWebProxy proxy, CredentialType credentialType, bool retrying)
        {
            NetworkCredential cred = null;
            if(credentialType == CredentialType.RequestCredentials) { }
                cred = new NetworkCredential(uname,pass);
            return cred;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using NuGet.Lucene.Web.Models;
using NuGet.Lucene.Web.Util;

namespace NuGet.Lucene.Web
{
    public static class MirroringPackageRepositoryFactory
    {
        private const string UserAgent = "NuGet.Lucene.Web";

        public static IMirroringPackageRepository Create(IPackageRepository localRepository, string remotePackageUrl, TimeSpan timeout, bool alwaysCheckMirror)
        {
            if (string.IsNullOrWhiteSpace(remotePackageUrl))
            {
                return new NonMirroringPackageRepository(localRepository);
            }

            //while (!Debugger.IsAttached){Thread.Sleep(100);} ; Console.WriteLine("Debugger attached");
            var remoteRepositoryNames = remotePackageUrl.Split(';');
            string pattern = @"^(https?://)(.*)@(.*)$";
            var regex = new Regex(pattern);
            var repos = new List<DataServicePackageRepository>();

            foreach (var s in remoteRepositoryNames)
            {
                if (regex.IsMatch(s))
                {
                    Match m = regex.Match(s);
                    var credentials = m.Groups[2].Value.Split(':');
                    var url = m.Groups[1].Value + m.Groups[3].Value;
                    var creds = new BasicAuthCredentialProvider(credentials[0],credentials[1]);
                    
                    HttpClient.DefaultCredentialProvider = creds;
                    repos.Add(CreateDataServicePackageRepository(new HttpClient(new Uri(url)), timeout));
                }
                else
                {
                    repos.Add(CreateDataServicePackageRepository(new HttpClient(new Uri(s)), timeout));
                }
            }
            DataServicePackageRepository[] remoteRepositories = repos.ToArray();

            if (alwaysCheckMirror)
            {
                return new EagerMirroringPackageRepository(localRepository, remoteRepositories, new WebCache());
            }

            return new MirroringPackageRepository(localRepository, remoteRepositories, new WebCache());
        }

        public static DataServicePackageRepository CreateDataServicePackageRepository(IHttpClient httpClient, TimeSpan timeout)
        {
            var userAgent = string.Format("{0}/{1} ({2})",
                                          UserAgent,
                                          typeof(MirroringPackageRepositoryFactory).Assembly.GetName().Version,
                                          Environment.OSVersion);

            var remoteRepository = new DataServicePackageRepository(httpClient);

            remoteRepository.SendingRequest += (s, e) =>
            {
                e.Request.Timeout = (int)timeout.TotalMilliseconds;

                ((HttpWebRequest)e.Request).UserAgent = userAgent;

                e.Request.Headers.Add(RepositoryOperationNames.OperationHeaderName, RepositoryOperationNames.Mirror);
            };

            return remoteRepository;
        }

    }

}

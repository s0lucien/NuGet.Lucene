﻿using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.OData.Query;
using System.Web.Http.Results;
using Lucene.Net.Linq;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Moq;
using NuGet.Lucene.Tests;
using NuGet.Lucene.Web.Models;
using NuGet.Lucene.Web.Util;
using NUnit.Framework;

namespace NuGet.Lucene.Web.Tests.Controllers
{
    [TestFixture]
    public class PackagesODataControllerTests : PackagesODataControllerTestBase
    {
        private TestPackage[] packages;

        [SetUp]
        public void SetUp()
        {
            packages = new[] { new TestPackage("a", "1.0"), new TestPackage("b", "2.0"), };
        }

        [Test]
        public void GetPackages()
        {
            repo.Setup(r => r.GetPackages()).Returns(packages.AsQueryable()).Verifiable();

            var result = controller.Get();

            Assert.That(result.Count(), Is.EqualTo(packages.Count()));
            repo.VerifyAll();
        }

        [Test]
        public void GetPackagesSkipSelect()
        {
            var provider = new LuceneDataProvider(new RAMDirectory(), Version.LUCENE_30);
            var options = SetUpRequestWithOptions("/api/odata/Packages?$select=Id&$skip=1");

            var queryable = provider.AsQueryable(() => new LucenePackage(_ => new MemoryStream())).Select(x => x.ToODataPackage());
            var result = options.ApplyTo(queryable, new ODataQuerySettings {HandleNullPropagation = HandleNullPropagationOption.False});
            
            Assert.That(result.OfType<object>().ToList().Count(), Is.EqualTo(0));
            repo.VerifyAll();
        }

        [Test]
        public void GetPackage()
        {
            repo.Setup(r => r.FindPackage("a", new SemanticVersion("1.0"))).Returns(packages[0]).Verifiable();

            var result = controller.Get("a", "1.0");

            Assert.That(result, Is.InstanceOf<OkNegotiatedContentResult<ODataPackage>>());
            var package = ((OkNegotiatedContentResult<ODataPackage>) result).Content;

            Assert.That(package, Is.Not.Null, "Content");
            Assert.That(package.Id, Is.EqualTo("a"));
            repo.VerifyAll();
        }

        [Test]
        public void GetPackage_InvalidId()
        {
            var result = controller.Get("", "1.0");

            Assert.That(result, Is.InstanceOf<BadRequestErrorMessageResult>());

            repo.Verify(r => r.FindPackage(It.IsAny<string>(), It.IsAny<SemanticVersion>()), Times.Never);
        }

        [Test]
        public void GetPackage_InvalidVersion()
        {
            var result = controller.Get("a", "OnePointOh");

            Assert.That(result, Is.InstanceOf<BadRequestErrorMessageResult>());

            repo.Verify(r => r.FindPackage(It.IsAny<string>(), It.IsAny<SemanticVersion>()), Times.Never);
        }

        [Test]
        public async Task GetCount()
        {
            repo.Setup(r => r.GetPackages()).Returns(packages.AsQueryable()).Verifiable();
            var options = SetUpRequestWithOptions("/api/odata/Packages()/$count");

            var response = controller.GetCount(options);

            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(packages.Count().ToString()));
        }
    }
}

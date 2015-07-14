using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Index;
using Lucene.Net.Linq;
using Lucene.Net.Search;
using Moq;
using NUnit.Framework;

namespace NuGet.Lucene.Tests
{
    [TestFixture]
    public class PackageIndexerSynchronizeTests : PackageIndexerTestBase
    {
        private static readonly string[] Empty = new string[0];
        private Mock<ISession<LucenePackage>> session;

        [SetUp]
        public void SetUp()
        {
            session = new Mock<ISession<LucenePackage>>();
            session.Setup(s => s.Query()).Returns(datasource);

            indexer.FakeSession = session.Object;
            indexer.Initialize();
        }

        [Test]
        public async Task DoesNothingOnNoDifferences()
        {
            await indexer.SynchronizeIndexWithFileSystemAsync(new IndexDifferences(Empty, Empty, Empty), CancellationToken.None);

            session.Verify();
        }

        [Test]
        public async Task DeletesMissingPackages()
        {
            var missing = new[] {"A.nupkg", "B.nupkg"};

            var deletedTerms = new List<Term>();

            session.Setup(s => s.Delete(It.IsAny<Query[]>())).Callback((Query[] query) =>
                deletedTerms.AddRange(query.Cast<TermQuery>().Select(q => q.Term)));

            await indexer.SynchronizeIndexWithFileSystemAsync(new IndexDifferences(Empty, missing, Empty), CancellationToken.None);

            session.Verify(s => s.Commit(), Times.AtLeastOnce());

            Assert.That(deletedTerms, Is.EquivalentTo(new[] { new Term("Path", "A.nupkg"), new Term("Path", "B.nupkg") }));
        }

        [Test]
        public async Task AddsNewPackages()
        {
            var newPackages = new[] { "A.1.0.nupkg" };

            var pkg = MakeSamplePackage("A", "1.0");
            loader.Setup(l => l.LoadFromFileSystem(newPackages[0])).Returns(pkg);

            session.Setup(s => s.Add(KeyConstraint.None, It.IsAny<LucenePackage>())).Verifiable();

            session.Setup(s => s.Commit()).Verifiable();

            await indexer.SynchronizeIndexWithFileSystemAsync(new IndexDifferences(newPackages, Empty, Empty), CancellationToken.None);

            session.VerifyAll();
        }

        [Test]
        public async Task PreservesDownloadCountOnModifiedPackage()
        {
            var currentPackage = MakeSamplePackage("A", "1.0");
            currentPackage.DownloadCount = 123;

            var updatedPackage = await SimulateUpdatePackage(currentPackage);

            Assert.That(updatedPackage.DownloadCount, Is.EqualTo(currentPackage.DownloadCount));
        }

        [Test]
        public async Task PreservesVersionDownloadCountOnModifiedPackage()
        {
            var currentPackage = MakeSamplePackage("A", "1.0");
            currentPackage.VersionDownloadCount = 456;

            var updatedPackage = await SimulateUpdatePackage(currentPackage);

            Assert.That(updatedPackage.VersionDownloadCount, Is.EqualTo(currentPackage.VersionDownloadCount));
        }

        [Test]
        public async Task PreservesOrigin()
        {
            var currentPackage = MakeSamplePackage("A", "1.0");
            currentPackage.OriginUrl = new Uri("http://example.com/nuget/");

            var updatedPackage = await SimulateUpdatePackage(currentPackage);

            Assert.That(updatedPackage.OriginUrl, Is.EqualTo(currentPackage.OriginUrl));
        }

        [Test]
        public async Task ContinuesOnException()
        {
            var newPackages = new[] { "A.1.0.nupkg", "B.1.0.nupkg" };

            var pkg = MakeSamplePackage("B", "1.0");

            loader.Setup(l => l.LoadFromFileSystem(newPackages[0])).Throws(new Exception("invalid package"));
            loader.Setup(l => l.LoadFromFileSystem(newPackages[1])).Returns(pkg);

            session.Setup(s => s.Add(KeyConstraint.None, It.IsAny<LucenePackage>())).Verifiable();

            session.Setup(s => s.Commit()).Verifiable();

            try
            {
                await
                    indexer.SynchronizeIndexWithFileSystemAsync(new IndexDifferences(newPackages, Empty, Empty),
                        CancellationToken.None);
            }
            catch (Exception ex)
            {
                Assert.That(ex.Message, Is.EqualTo("The package file 'A.1.0.nupkg' could not be loaded."));
                Assert.That(ex.InnerException.Message, Is.EqualTo("invalid package"));
            }

            loader.VerifyAll();
            session.VerifyAll();
        }

        [Test]
        public async Task ThrowsAggregateExceptionOnMultipleFailures()
        {
            var newPackages = new[] { "A.1.0.nupkg", "B.1.0.nupkg" };

            loader.Setup(l => l.LoadFromFileSystem(newPackages[0])).Throws(new Exception("invalid package"));
            loader.Setup(l => l.LoadFromFileSystem(newPackages[1])).Throws(new Exception("unsupported package"));

            try
            {
                await
                    indexer.SynchronizeIndexWithFileSystemAsync(new IndexDifferences(newPackages, Empty, Empty),
                        CancellationToken.None);
            }
            catch (AggregateException ex)
            {
                Assert.That(ex.InnerExceptions.Count, Is.EqualTo(2));
                Assert.That(ex.InnerExceptions.Select(e => e.Message).ToArray(), Is.EquivalentTo(new[]
                {
                    "The package file 'A.1.0.nupkg' could not be loaded.",
                    "The package file 'B.1.0.nupkg' could not be loaded."
                }));
            }
        }

        private async Task<LucenePackage> SimulateUpdatePackage(LucenePackage currentPackage)
        {
            var date = new DateTimeOffset(2015, 1, 29, 0, 0, 0, TimeSpan.Zero);
            var modifiedPackages = new[] { "A.1.0.nupkg" };
            currentPackage.Published = date;

            InsertPackage(currentPackage);

            var newPackage = MakeSamplePackage("A", "1.0");
            newPackage.Published = date.AddDays(1);
            loader.Setup(l => l.LoadFromFileSystem(modifiedPackages[0])).Returns(newPackage);

            LucenePackage updatedPackage = null;

            session.Setup(s => s.Add(KeyConstraint.Unique, It.IsAny<LucenePackage>()))
                .Callback<KeyConstraint, LucenePackage[]>((c, p) => { updatedPackage = p.FirstOrDefault(); })
                .Verifiable();

            session.Setup(s => s.Commit()).Verifiable();

            await
                indexer.SynchronizeIndexWithFileSystemAsync(new IndexDifferences(Empty, Empty, modifiedPackages),
                    CancellationToken.None);

            session.VerifyAll();
            return updatedPackage;
        }

    }
}

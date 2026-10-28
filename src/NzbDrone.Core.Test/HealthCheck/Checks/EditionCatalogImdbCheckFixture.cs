// krzw(edition-runtime-catalog): IMDb runtime provider health check
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Movies.Editions.Catalog.Providers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class EditionCatalogImdbCheckFixture : CoreTest<EditionCatalogImdbCheck>
    {
        private ImdbRuntimeProviderStatus _status;

        [SetUp]
        public void Setup()
        {
            _status = new ImdbRuntimeProviderStatus();
            Mocker.SetConstant<IImdbRuntimeProviderStatus>(_status);

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                  .Returns("Some Warning Message");

            Mocker.GetMock<IConfigService>().SetupGet(c => c.EditionCatalogImdbEnabled).Returns(true);
        }

        [Test]
        public void should_return_ok_without_any_batch()
        {
            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_return_ok_when_one_of_the_last_3_batches_succeeded()
        {
            _status.RecordBatch(false);
            _status.RecordBatch(false);
            _status.RecordBatch(true);
            _status.RecordBatch(false);
            _status.RecordBatch(false);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_warn_when_the_last_3_batches_failed()
        {
            _status.RecordBatch(true);
            _status.RecordBatch(false);
            _status.RecordBatch(false);
            _status.RecordBatch(false);

            Subject.Check().ShouldBeWarning();
        }

        [Test]
        public void should_return_ok_when_imdb_provider_disabled()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EditionCatalogImdbEnabled).Returns(false);

            _status.RecordBatch(false);
            _status.RecordBatch(false);
            _status.RecordBatch(false);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_record_last_success()
        {
            _status.RecordBatch(true);

            NUnit.Framework.Assert.That(_status.LastSuccess.HasValue);
        }
    }
}

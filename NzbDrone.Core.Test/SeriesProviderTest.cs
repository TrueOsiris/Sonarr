﻿// ReSharper disable RedundantUsingDirective
using System;
using System.Linq;
using AutoMoq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Providers;
using NzbDrone.Core.Providers.Core;
using NzbDrone.Core.Repository;
using NzbDrone.Core.Repository.Quality;
using NzbDrone.Core.Test.Framework;
using PetaPoco;
using TvdbLib.Data;

// ReSharper disable InconsistentNaming

namespace NzbDrone.Core.Test
{
    [TestFixture]
    // ReSharper disable InconsistentNaming
    public class SeriesProviderTest : TestBase
    {
        [Test]
        public void Map_path_to_series()
        {
            //Arrange
            var fakeSeries = Builder<TvdbSeries>.CreateNew()
                .With(f => f.SeriesName = "The Simpsons")
                .Build();

            var fakeSearch = Builder<TvdbSearchResult>.CreateNew()
                .With(s => s.Id = fakeSeries.Id)
                .With(s => s.SeriesName = fakeSeries.SeriesName)
                .Build();


            var mocker = new AutoMoqer();

            mocker.GetMock<IDatabase>()
                .Setup(f => f.Exists<Series>(It.IsAny<String>()))
                .Returns(false);

            mocker.GetMock<TvDbProvider>()
                .Setup(f => f.GetSeries(It.IsAny<String>()))
                .Returns(fakeSearch);
            mocker.GetMock<TvDbProvider>()
                .Setup(f => f.GetSeries(fakeSeries.Id, false))
                .Returns(fakeSeries)
                .Verifiable();

            //Act

            var mappedSeries = mocker.Resolve<SeriesProvider>().MapPathToSeries(@"D:\TV Shows\The Simpsons");

            //Assert
            mocker.GetMock<TvDbProvider>().VerifyAll();
            Assert.AreEqual(fakeSeries, mappedSeries);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Add_new_series(bool useSeasonFolder)
        {
            var mocker = new AutoMoqer();

            mocker.GetMock<ConfigProvider>()
                .Setup(c => c.UseSeasonFolder).Returns(useSeasonFolder);

            mocker.SetConstant(MockLib.GetEmptyDatabase());




            const string path = "C:\\Test\\";
            const int tvDbId = 1234;
            const int qualityProfileId = 2;

            //Act
            var seriesProvider = mocker.Resolve<SeriesProvider>();
            seriesProvider.AddSeries(path, tvDbId, qualityProfileId);



            //Assert
            var series = seriesProvider.GetAllSeries();
            series.Should().HaveCount(1);
            Assert.AreEqual(path, series.First().Path);
            Assert.AreEqual(tvDbId, series.First().SeriesId);
            Assert.AreEqual(qualityProfileId, series.First().QualityProfileId);
            series.First().SeasonFolder.Should().Be(useSeasonFolder);
        }

        [Test]
        public void find_series_empty_repo()
        {
            var mocker = new AutoMoqer();
            mocker.SetConstant(MockLib.GetEmptyDatabase());

            //Act
            var seriesProvider = mocker.Resolve<SeriesProvider>();
            var series = seriesProvider.FindSeries("My Title");


            //Assert
            Assert.IsNull(series);
        }


        [Test]
        public void Get_series_invalid_series_id_should_return_null()
        {
            var mocker = new AutoMoqer();
            mocker.SetConstant(MockLib.GetEmptyDatabase());

            //Act
            var seriesProvider = mocker.Resolve<SeriesProvider>();
            var series = seriesProvider.GetSeries(2);


            //Assert
            Assert.IsNull(series);
        }

        [Test]
        public void Get_series_by_id()
        {
            var mocker = new AutoMoqer(MockBehavior.Strict);
            var db = MockLib.GetEmptyDatabase();
            mocker.SetConstant(db);

            var fakeSeries = Builder<Series>.CreateNew().With(c => c.QualityProfileId = 1).Build();
            var fakeQuality = Builder<QualityProfile>.CreateNew().Build();

            var id = db.Insert(fakeSeries);
            db.Insert(fakeQuality);

            //Act
            mocker.Resolve<QualityProvider>();
            var series = mocker.Resolve<SeriesProvider>().GetSeries(1);

            //Assert
            series.ShouldHave().AllPropertiesBut(s => s.QualityProfile, s => s.SeriesId);
            series.QualityProfile.Should().NotBeNull();
            series.QualityProfile.ShouldHave().Properties(q => q.Name, q => q.SonicAllowed, q => q.Cutoff, q => q.AllowedString);
        }

        [Test]
        public void Find_series_by_cleanName_mapped()
        {
            var mocker = new AutoMoqer(MockBehavior.Strict);
            var db = MockLib.GetEmptyDatabase();
            mocker.SetConstant(db);

            var fakeSeries = Builder<Series>.CreateNew()
                .With(c => c.QualityProfileId = 1)
                .With(c => c.CleanTitle = "laworder")
                .Build();
            var fakeQuality = Builder<QualityProfile>.CreateNew().Build();

            var id = db.Insert(fakeSeries);
            db.Insert(fakeQuality);

            //Act
            mocker.Resolve<QualityProvider>();
            mocker.GetMock<SceneMappingProvider>().Setup(s => s.GetSeriesId("laworder")).Returns(1);

            var series = mocker.Resolve<SeriesProvider>().FindSeries("laworder");

            //Assert
            series.ShouldHave().AllPropertiesBut(s => s.QualityProfile, s => s.SeriesId);
            series.QualityProfile.Should().NotBeNull();
            series.QualityProfile.ShouldHave().Properties(q => q.Name, q => q.SonicAllowed, q => q.Cutoff, q => q.AllowedString);
        }

        [Test]
        public void find_series_empty_match()
        {
            var mocker = new AutoMoqer();
            var emptyRepository = MockLib.GetEmptyDatabase();
            mocker.SetConstant(emptyRepository);
            emptyRepository.Insert(MockLib.GetFakeSeries(1, "MyTitle"));
            //Act
            var seriesProvider = mocker.Resolve<SeriesProvider>();
            var series = seriesProvider.FindSeries("WrongTitle");


            //Assert
            Assert.IsNull(series);
        }

        [TestCase("The Test", "Test")]
        [TestCase("Through the Wormhole", "Through.the.Wormhole")]
        public void find_series_match(string title, string searchTitle)
        {
            var mocker = new AutoMoqer();
            var emptyRepository = MockLib.GetEmptyDatabase();
            mocker.SetConstant(emptyRepository);
            emptyRepository.Insert(MockLib.GetFakeSeries(1, title));
            emptyRepository.Insert(Builder<QualityProfile>.CreateNew().Build());
            mocker.Resolve<QualityProvider>();

            //Act
            var seriesProvider = mocker.Resolve<SeriesProvider>();
            var series = seriesProvider.FindSeries(searchTitle);

            //Assert
            Assert.IsNotNull(series);
            Assert.AreEqual(title, series.Title);
            series.QualityProfile.Should().NotBeNull();
            series.QualityProfile.ShouldHave().Properties(q => q.Name, q => q.SonicAllowed, q => q.Cutoff, q => q.AllowedString);
        }

        [Test]
        public void is_monitored()
        {
            var mocker = new AutoMoqer();

            var db = MockLib.GetEmptyDatabase();
            mocker.SetConstant(db);

            mocker.SetConstant(db);

            db.Insert(Builder<Series>.CreateNew()
                                                  .With(c => c.Monitored = true)
                                                  .With(c => c.SeriesId = 12)
                                                  .Build());

            db.Insert(Builder<Series>.CreateNew()
                                                 .With(c => c.Monitored = false)
                                                 .With(c => c.SeriesId = 11)
                                                 .Build());


            //Act, Assert
            var provider = mocker.Resolve<SeriesProvider>();
            Assert.IsTrue(provider.IsMonitored(12));
            Assert.IsFalse(provider.IsMonitored(11));
            Assert.IsFalse(provider.IsMonitored(1));
        }



    }
}                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             
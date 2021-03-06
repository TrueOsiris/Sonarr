﻿using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class CompletedDownloadServiceFixture : CoreTest<CompletedDownloadService>
    {
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            var completed = Builder<DownloadClientItem>.CreateNew()
                                                    .With(h => h.Status = DownloadItemStatus.Completed)
                                                    .With(h => h.OutputPath = new OsPath(@"C:\DropFolder\MyDownload".AsOsAgnostic()))
                                                    .With(h => h.Title = "Drone.S01E01.HDTV")
                                                    .Build();

            var remoteEpisode = new RemoteEpisode
                                {
                                    Series = new Series(),
                                    Episodes = new List<Episode> { new Episode { Id = 1 } }
                                };

            _trackedDownload = Builder<TrackedDownload>.CreateNew()
                    .With(c => c.State = TrackedDownloadStage.Downloading)
                    .With(c => c.DownloadItem = completed)
                    .With(c => c.RemoteEpisode = remoteEpisode)
                    .Build();


            Mocker.GetMock<IDownloadClient>()
              .SetupGet(c => c.Definition)
              .Returns(new DownloadClientDefinition { Id = 1, Name = "testClient" });

            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(c => c.Get(It.IsAny<int>()))
                  .Returns(Mocker.GetMock<IDownloadClient>().Object);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.MostRecentForDownloadId(_trackedDownload.DownloadItem.DownloadId))
                  .Returns(new History.History());

        }

        private void GivenNoGrabbedHistory()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.MostRecentForDownloadId(_trackedDownload.DownloadItem.DownloadId))
                .Returns((History.History)null);
        }


        private void GivenSuccessfulImport()
        {
            Mocker.GetMock<IDownloadedEpisodesImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<DownloadClientItem>()))
                .Returns(new List<ImportResult>
                    {
                        new ImportResult(new ImportDecision(new LocalEpisode() { Path = @"C:\TestPath\Droned.S01E01.mkv" }))
                    });
        }


        [TestCase(DownloadItemStatus.Downloading)]
        [TestCase(DownloadItemStatus.Failed)]
        [TestCase(DownloadItemStatus.Queued)]
        [TestCase(DownloadItemStatus.Paused)]
        [TestCase(DownloadItemStatus.Warning)]
        public void should_not_process_if_download_status_isnt_completed(DownloadItemStatus status)
        {
            _trackedDownload.DownloadItem.Status = status;

            Subject.Process(_trackedDownload);

            AssertNoAttemptedImport();
        }


        [Test]
        public void should_not_process_if_matching_history_is_not_found_and_no_category_specified()
        {
            _trackedDownload.DownloadItem.Category = null;
            GivenNoGrabbedHistory();

            Subject.Process(_trackedDownload);

            AssertNoAttemptedImport();
        }

        [Test]
        public void should_process_if_matching_history_is_not_found_but_category_specified()
        {
            _trackedDownload.DownloadItem.Category = "tv";
            GivenNoGrabbedHistory();
            GivenSuccessfulImport();

            Subject.Process(_trackedDownload);

            AssertCompletedDownload();
        }



        [Test]
        public void should_not_process_if_storage_directory_in_drone_factory()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(v => v.DownloadedEpisodesFolder)
                  .Returns(@"C:\DropFolder".AsOsAgnostic());

            _trackedDownload.DownloadItem.OutputPath = new OsPath(@"C:\DropFolder\SomeOtherFolder".AsOsAgnostic());

            Subject.Process(_trackedDownload);

            AssertNoAttemptedImport();
        }


        [Test]
        public void should_not_process_if_output_path_is_empty()
        {
            _trackedDownload.DownloadItem.OutputPath = new OsPath();

            Subject.Process(_trackedDownload);

            AssertNoAttemptedImport();
        }


        [Test]
        public void should_not_mark_as_imported_if_all_files_were_rejected()
        {
            Mocker.GetMock<IDownloadedEpisodesImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(new ImportDecision(new LocalEpisode {Path = @"C:\TestPath\Droned.S01E01.mkv"}, "Rejected!"),"Test Failure"),
                               new ImportResult(new ImportDecision(new LocalEpisode {Path = @"C:\TestPath\Droned.S01E02.mkv"}, "Rejected!"),"Test Failure")
                           });

            Subject.Process(_trackedDownload);


            _trackedDownload.State.Should().NotBe(TrackedDownloadStage.Imported);

            AssertNoCompletedDownload();
        }

        [Test]
        public void should_not_mark_as_imported_if_all_files_were_skipped()
        {
            Mocker.GetMock<IDownloadedEpisodesImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(new ImportDecision(new LocalEpisode {Path = @"C:\TestPath\Droned.S01E01.mkv"}),"Test Failure"),
                               new ImportResult(new ImportDecision(new LocalEpisode {Path = @"C:\TestPath\Droned.S01E01.mkv"}),"Test Failure")
                           });


            Subject.Process(_trackedDownload);

            AssertNoCompletedDownload();
        }

        [Test]
        public void should_not_mark_as_imported_if_some_files_were_skipped()
        {
            Mocker.GetMock<IDownloadedEpisodesImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(new ImportDecision(new LocalEpisode {Path = @"C:\TestPath\Droned.S01E01.mkv"})),
                               new ImportResult(new ImportDecision(new LocalEpisode{Path = @"C:\TestPath\Droned.S01E01.mkv"}),"Test Failure")
                           });


            Subject.Process(_trackedDownload);

            AssertNoCompletedDownload();
        }


        private void AssertNoAttemptedImport()
        {
            Mocker.GetMock<IDownloadedEpisodesImportService>()
                .Verify(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<DownloadClientItem>()), Times.Never());

            AssertNoCompletedDownload();
        }

        private void AssertNoCompletedDownload()
        {
            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Never());

            _trackedDownload.State.Should().NotBe(TrackedDownloadStage.Imported);
        }

        private void AssertCompletedDownload()
        {
            Mocker.GetMock<IDownloadedEpisodesImportService>()
                .Verify(v => v.ProcessPath(_trackedDownload.DownloadItem.OutputPath.FullPath, _trackedDownload.DownloadItem), Times.Once());
            
            _trackedDownload.State.Should().Be(TrackedDownloadStage.Imported);
        }
    }
}

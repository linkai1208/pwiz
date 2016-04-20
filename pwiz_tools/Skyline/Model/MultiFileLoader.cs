﻿/*
 * Original author: Don Marsh <donmarsh .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2015 University of Washington - Seattle, WA
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using pwiz.Common.SystemUtil;
using pwiz.Skyline.Model.Results;
using pwiz.Skyline.Properties;
using pwiz.Skyline.Util;

namespace pwiz.Skyline.Model
{
    public class MultiFileLoader
    {
        private readonly QueueWorker<LoadInfo> _worker;
        private readonly Dictionary<MsDataFileUri, int> _loadingPaths;
        private readonly bool _synchronousMode;
        private int _threadCount;

        private MultiProgressStatus _status;

        public MultiFileLoader(bool synchronousMode)
        {
            _worker = new QueueWorker<LoadInfo>(null, LoadFile);
            _loadingPaths = new Dictionary<MsDataFileUri, int>();
            _synchronousMode = synchronousMode;
            _threadCount = 1;
            ResetStatus();
        }

        public MultiProgressStatus Status { get { return _status; } }

        public void ChangeStatus(ChromatogramLoadingStatus loadingStatus)
        {
            ChangeStatus(s => s.ChangeStatus(loadingStatus));
        }

        public void ResetStatus()
        {
            ChangeStatus(s => new MultiProgressStatus(_synchronousMode));
        }

        private void ChangeStatus(Func<MultiProgressStatus, MultiProgressStatus> change)
        {
            // Eventual consistency with immutable object to avoid the need for locking
            MultiProgressStatus statusOriginal, statusNew;
            do
            {
                statusOriginal = _status;
                statusNew = change(statusOriginal);
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _status, statusNew, statusOriginal), statusOriginal));
        }

        public void InitializeThreadCount()
        {
            switch (Settings.Default.ImportResultsSimultaneousFiles)
            {
                case 0:
                    _threadCount = 1;
                    break;

                case 1:
                    _threadCount = Math.Max(1, Environment.ProcessorCount / 4);
                    break;

                case 2:
                    _threadCount = Math.Max(1, Environment.ProcessorCount / 2);
                    break;
            }
        }

        /// <summary>
        /// Add the given file to the queue of files to load.
        /// </summary>
        public void Load(
            IList<DataFileReplicates> loadList,
            SrmDocument document,
            ChromatogramCache cacheRecalc,
            MultiFileLoadMonitor loadMonitor,
            Action<ChromatogramCache, IProgressStatus> complete)
        {
            // This may be called on multiple background loader threads simultaneously, but QueueWorker can handle it.
            _worker.RunAsync(_threadCount, "Load file"); // Not L10N

            lock (this)
            {
                // Find non-duplicate paths to load.
                var uniqueLoadList = new List<DataFileReplicates>();
                foreach (var loadItem in loadList)
                {
                    // Ignore a file that is already being loaded (or is queued for loading).
                    if (_loadingPaths.ContainsKey(loadItem.DataFile))
                        continue;
                    int idIndex = document.Id.GlobalIndex;
                    _loadingPaths.Add(loadItem.DataFile, idIndex);
                    uniqueLoadList.Add(loadItem);
                }

                if (uniqueLoadList.Count == 0)
                    return;

                // Add new paths to queue.
                foreach (var loadItem in uniqueLoadList)
                {
                    var loadingStatus = new ChromatogramLoadingStatus(loadItem.DataFile, loadItem.ReplicateList);

                    ChangeStatus(s => s.Add(loadingStatus));

                    // Queue work item to load the file.
                    _worker.Add(new LoadInfo
                    {
                        Path = loadItem.DataFile,
                        PartPath = loadItem.PartPath,
                        Document = document,
                        CacheRecalc = cacheRecalc,
                        Status = loadingStatus,
                        LoadMonitor = new SingleFileLoadMonitor(loadMonitor, loadItem.DataFile),
                        Complete = complete
                    });
                }
            }
        }

        public void DoneAddingFiles()
        {
            _worker.DoneAdding(true);
        }

        public void ClearFile(MsDataFileUri filePath)
        {
            lock (this)
            {
                _loadingPaths.Remove(filePath);
                if (_loadingPaths.Count == 0)
                    ResetStatus();
            }
        }

        public void ClearDocument(SrmDocument document)
        {
            lock (this)
            {
                foreach (var filePath in _loadingPaths.Where(p => p.Value == document.Id.GlobalIndex).Select(p => p.Key).ToArray())
                {
                    _loadingPaths.Remove(filePath);
                }
                if (_loadingPaths.Count == 0)
                    ResetStatus();
            }
        }

        public bool IsLoading(SrmDocument document)
        {
            lock (this)
            {
                return _loadingPaths.ContainsValue(document.Id.GlobalIndex);
            }
        }

        private class LoadInfo
        {
            public MsDataFileUri Path;
            public string PartPath;
            public SrmDocument Document;
            public ChromatogramCache CacheRecalc;
            public IProgressStatus Status;
            public ILoadMonitor LoadMonitor;
            public Action<ChromatogramCache, IProgressStatus> Complete;
        }

        private void LoadFile(LoadInfo loadInfo, int threadIndex)
        {
            ChromatogramCache.Build(
                loadInfo.Document,
                loadInfo.CacheRecalc,
                loadInfo.PartPath,
                loadInfo.Path,
                loadInfo.Status,
                loadInfo.LoadMonitor,
                loadInfo.Complete);

            var loadingStatus = loadInfo.Status as ChromatogramLoadingStatus;
            if (loadingStatus != null)
                loadingStatus.Transitions.Flush();
        }
    }

    public class MultiFileLoadMonitor : BackgroundLoader.LoadMonitor
    {
        private readonly ChromatogramManager _chromatogramManager;
        public MultiFileLoadMonitor(ChromatogramManager manager, IDocumentContainer container, object tag)
            : base(manager, container, tag)
        {
            _chromatogramManager = manager;
        }

        public override UpdateProgressResponse UpdateProgress(IProgressStatus status)
        {
            var loadingStatus = status as ChromatogramLoadingStatus;
            if (loadingStatus != null)
            {
                _chromatogramManager.ChangeStatus(loadingStatus);
                status = _chromatogramManager.Status;
            }
            var progressResult = _chromatogramManager.UpdateProgress(status);
            return progressResult;
        }

        public bool IsCanceledFile(MsDataFileUri filePath)
        {
            return IsCanceledItem(filePath);
        }
    }

    public class SingleFileLoadMonitor : BackgroundLoader.LoadMonitor
    {
        private readonly MultiFileLoadMonitor _loadMonitor;
        private readonly MsDataFileUri _dataFile;

        public SingleFileLoadMonitor(MultiFileLoadMonitor loadMonitor, MsDataFileUri dataFile)
        {
            _loadMonitor = loadMonitor;
            _dataFile = dataFile;
            HasUI = loadMonitor.HasUI;
        }

        public override IStreamManager StreamManager
        {
            get { return _loadMonitor.StreamManager; }
        }

        public override bool IsCanceled
        {
            get
            {
                return _loadMonitor.IsCanceledFile(_dataFile);
            }
        }

        public override UpdateProgressResponse UpdateProgress(IProgressStatus status)
        {
            return _loadMonitor.UpdateProgress(status);
        }
    }
}

/*
 * Original author: Brendan MacLean <brendanx .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2009 University of Washington - Seattle, WA
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
using System.IO;
using pwiz.Common.Collections;
using pwiz.Common.SystemUtil;
using pwiz.Skyline.Properties;
using pwiz.Skyline.Util;
using pwiz.Skyline.Util.Extensions;

namespace pwiz.Skyline.Model.Results
{
    internal class ChromCacheWriter : IDisposable
    {
        private readonly Action<ChromatogramCache, IProgressStatus> _completed;

        protected readonly List<ChromCachedFile> _listCachedFiles = new List<ChromCachedFile>();
        protected readonly List<ChromTransition> _listTransitions = new List<ChromTransition>();
        protected readonly List<ChromGroupHeaderInfo> _listGroups = new List<ChromGroupHeaderInfo>();
        protected readonly List<byte> _listTextIdBytes = new List<byte>();
        protected readonly List<Type> _listScoreTypes = new List<Type>();
        protected readonly FileSaver _fs;
        protected readonly FileSaver _fsScans;
        protected readonly FileSaver _fsPeaks;
        protected readonly FileSaver _fsScores;
        protected readonly ILoadMonitor _loader;
        protected IProgressStatus _status;
        protected int _peakCount;
        protected int _scoreCount;
        protected IPooledStream _destinationStream;

        protected ChromCacheWriter(string cachePath, ILoadMonitor loader, IProgressStatus status,
                                   Action<ChromatogramCache, IProgressStatus> completed)
        {
            CachePath = cachePath;
            _fs = new FileSaver(CachePath);
            _fsScans = new FileSaver(CachePath + ChromatogramCache.SCANS_EXT, true);
            _fsPeaks = new FileSaver(CachePath + ChromatogramCache.PEAKS_EXT, true);
            _fsScores = new FileSaver(CachePath + ChromatogramCache.SCORES_EXT, true);
            _loader = loader;
            _status = status;
            _completed = completed;
        }

        protected string CachePath { get; private set; }

        protected void Complete(Exception x)
        {
            lock (this)
            {
                ChromatogramCache result = null;
                try
                {
                    if (x == null && !_status.IsFinal)
                    {
                        long locationScanIds = 0, countBytesScanIds = 0;
                        if (_fs.Stream != null)
                        {
                            try
                            {
                                locationScanIds = _fs.Stream.Position;
                                countBytesScanIds = _fsScans.Stream.Position;

                                ChromatogramCache.WriteStructs(_fs.Stream,
                                                               _fsScans.Stream,
                                                               _fsPeaks.Stream,
                                                               _fsScores.Stream,
                                                               _listCachedFiles,
                                                               _listGroups,
                                                               _listTransitions,
                                                               _listTextIdBytes,
                                                               _listScoreTypes,
                                                               _scoreCount,
                                                               _peakCount);

                                _loader.StreamManager.Finish(_fs.Stream);
                                _fs.Stream = null;
                                // Allow cancelation right up to the final commit
                                if (!_loader.IsCanceled)
                                    _fs.Commit(_destinationStream);
                                else
                                {
                                    _loader.UpdateProgress(_status = _status.Cancel());
                                }
                            }
                            catch (Exception xWrite)
                            {
                                throw new IOException(TextUtil.LineSeparate(string.Format(Resources.ChromCacheWriter_Complete_Failure_attempting_to_write_the_file__0_, _fs.RealName), xWrite.Message));
                            }
                        }

                        if (!_status.IsCanceled)
                        {
                            // Create stream identifier, but do not open.  The stream will be opened
                            // the first time the document uses it.
                            var readStream = _loader.StreamManager.CreatePooledStream(CachePath, false);

                            // DebugLog.Info("{0}. {1} - created", readStream.GlobalIndex, CachePath);

                            _fsPeaks.Stream.Seek(0, SeekOrigin.Begin);
                            _fsScores.Stream.Seek(0, SeekOrigin.Begin);
                            var rawData = new ChromatogramCache.RawData
                            {
                                FormatVersion = ChromatogramCache.FORMAT_VERSION_CACHE,
                                ChromCacheFiles = _listCachedFiles.ToArray(),
                                ChromatogramEntries = _listGroups.ToArray(),
                                ChromTransitions = _listTransitions.ToArray(),
                                ChromatogramPeaks = new BlockedArray<ChromPeak>(
                                    count => ChromPeak.ReadArray(_fsPeaks.FileStream, count), _peakCount,
                                    ChromPeak.SizeOf, ChromPeak.DEFAULT_BLOCK_SIZE),
                                ScoreTypes = _listScoreTypes.ToArray(),
                                Scores = new BlockedArray<float>(
                                    count => PrimitiveArrays.Read<float>(_fsScores.FileStream, count), _scoreCount,
                                    sizeof(float), ChromatogramCache.DEFAULT_SCORES_BLOCK_SIZE),
                                TextIdBytes = _listTextIdBytes.ToArray(),
                                LocationScanIds = locationScanIds,
                                CountBytesScanIds = countBytesScanIds,
                            };
                            result = new ChromatogramCache(CachePath, rawData, readStream);
                            _status = _status.Complete();
                            _loader.UpdateProgress(_status);
                        }
                    }
                }
                catch (Exception x2)
                {
                    x = x2;
                }
                finally
                {
                    Dispose();
                }

                try
                {
                    _completed(result, x == null ? _status : _status.ChangeErrorException(x));
                }
                catch (Exception x2)
                {
                    _completed(null, _status.ChangeErrorException(x2));
                }
            }
        }

        public virtual void Dispose()
        {
            if (_fs.Stream != null)
            {
                try { _loader.StreamManager.Finish(_fs.Stream); }
                catch (IOException) { }
                _fs.Stream = null;
            }
            _fs.Dispose();
            _fsPeaks.Dispose();
            _fsScans.Dispose();
            _fsScores.Dispose();
        }
    }
}

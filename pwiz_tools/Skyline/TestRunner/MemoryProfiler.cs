﻿/*
 * Original author: Don Marsh <donmarsh .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2013 University of Washington - Seattle, WA
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

using JetBrains.Profiler.Api;

// ReSharper disable LocalizableElement
namespace TestRunner
{
    /// <summary>
    /// MemoryProfiler creates memory snapshots if the JetBrains DotMemory is running.
    /// </summary>
    public static class MemoryProfiler
    {
        /// <summary>
        /// Take a memory snapshot.
        /// </summary>
        public static void Snapshot(string name)
        {
            if (0 != (JetBrains.Profiler.Api.MemoryProfiler.GetFeatures() & MemoryFeatures.Ready))
            {
                // Uncomment to start collecting the stack traces of all allocations.
                //JetBrains.Profiler.Api.MemoryProfiler.CollectAllocations(true);

                JetBrains.Profiler.Api.MemoryProfiler.GetSnapshot(name);
            }
            // Consider: support other types of profilers.
        }
    }
}

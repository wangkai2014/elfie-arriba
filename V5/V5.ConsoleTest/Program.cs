﻿using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using Microsoft.CodeAnalysis.Elfie.Extensions;
using Microsoft.CodeAnalysis.Elfie.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using V5;
using V5.Collections;
using V5.ConsoleTest.Model;
using V5.Data;
using V5.Serialization;

namespace V5.ConsoleTest
{
    public class WebRequestDatabase
    {
        public const int ParallelCount = 4;

        public PrimitiveColumn<long> EventTime;
        public PrimitiveColumn<ushort> HttpStatus;
        public PrimitiveColumn<int> ResponseBytes;

        public SortBucketColumn<long> EventTimeBuckets;
        public SortBucketColumn<ushort> HttpStatusBuckets;
        public SortBucketColumn<int> ResponseBytesBuckets;

        public WebRequestDatabase(long capacity)
        {
            this.EventTime = new PrimitiveColumn<long>("EventTime", new long[capacity]);
            this.HttpStatus = new PrimitiveColumn<ushort>("HttpStatus", new ushort[capacity]);
            this.ResponseBytes = new PrimitiveColumn<int>("ResponseBytes", new int[capacity]);
        }

        public int Count => this.EventTime.Count;

        public void Index(Random r)
        {
            this.EventTimeBuckets = SortBucketColumn<long>.Build("EventTime", this.EventTime.Values, 255, r, ParallelCount);
            this.HttpStatusBuckets = SortBucketColumn<ushort>.Build("HttpStatus", this.HttpStatus.Values, 255, r, ParallelCount);
            this.ResponseBytesBuckets = SortBucketColumn<int>.Build("ResponseBytes", this.ResponseBytes.Values, 255, r, ParallelCount);
        }

        public void Load(string filePath)
        {
            this.EventTime = PrimitiveColumn<long>.Read(filePath, "EventTime");
            this.HttpStatus = PrimitiveColumn<ushort>.Read(filePath, "HttpStatus");
            this.ResponseBytes = PrimitiveColumn<int>.Read(filePath, "ResponseBytes");

            this.EventTimeBuckets = SortBucketColumn<long>.Read(filePath, "EventTime");
            this.HttpStatusBuckets = SortBucketColumn<ushort>.Read(filePath, "HttpStatus");
            this.ResponseBytesBuckets = SortBucketColumn<int>.Read(filePath, "ResponseBytes");
        }

        public void Save(string filePath)
        {
            this.EventTime.Write(filePath);
            this.HttpStatus.Write(filePath);
            this.ResponseBytes.Write(filePath);

            this.EventTimeBuckets.Write(filePath);
            this.HttpStatusBuckets.Write(filePath);
            this.ResponseBytesBuckets.Write(filePath);
        }
    }

    class Program
    {
        public const int ParallelCount = 2;
        public const string PartitionPath = @"..\..\..\DiskCache\Tables\Person\0";

        static void Main(string[] args)
        {
            PerformanceTests();
            return;

            int rowCount = 8 * 1000 * 1000;
            WebRequestDatabase db = new WebRequestDatabase(rowCount);
            V0.WebRequestDatabase db0 = new V0.WebRequestDatabase();

            if (Directory.Exists(PartitionPath))
            {
                using (new TraceWatch("Loading Database..."))
                {
                    db.Load(PartitionPath);
                    Trace.WriteLine($" -> {db.Count:n0} rows");
                }

                using (new TraceWatch("Indexing Database [not needed in load]..."))
                {
                    db.Index(new Random(0));
                }
            }
            else
            {
                List<WebRequest> data = null;

                using (new TraceWatch($"Generating {rowCount:n0} sample rows..."))
                {
                    WebRequestGenerator g = new WebRequestGenerator(new Random(5), DateTime.UtcNow.AddMonths(-6), 250);
                    data = g.Next(rowCount);
                }

                db0.Requests = data;

                using (new TraceWatch("Copying into Database..."))
                {
                    for (int i = 0; i < rowCount; ++i)
                    {
                        WebRequest row = data[i];
                        db.EventTime.Values[i] = row.EventTime.Ticks;
                        db.HttpStatus.Values[i] = row.HttpStatus;
                        db.ResponseBytes.Values[i] = row.ResponseBytes;
                    }
                }

                using (new TraceWatch("Indexing Database..."))
                {
                    db.Index(new Random(0));
                }

                using (new TraceWatch("Saving Database..."))
                {
                    db.Save(PartitionPath);
                }
            }

            IndexSet set = new IndexSet(db.Count);
            Span<int> page = new Span<int>(new int[4096]);

            IndexSet[] sets = new IndexSet[ParallelCount];
            Span<int>[] pages = new Span<int>[ParallelCount];
            for(int i = 0; i < ParallelCount; ++i)
            {
                sets[i] = new IndexSet(db.Count / ParallelCount);
                pages[i] = new Span<int>(new int[4096]);
            }

            Benchmark.Compare("HttpStatus = 404 AND ResponseBytes > 1000", 20, db.Count, new string[] { "Managed Direct", "Managed Column", "V5.Native" },
                () => QueryManagedDirect(db, set),
                () => QueryManagedColumn(db, set, page),
                () => QueryV5(db, sets, pages)
            );

            PerformanceTests();
        }

        static void PerformanceTests()
        {
            int iterations = 1000;
            int size = 8 * 1000 * 1000;

            //long[] array = new long[size];
            //Benchmark.Compare("ArrayExtensions", 10, size, new string[] { "WriteArray", "ReadArray" },
            //    () => 
            //    {
            //        using (BinaryWriter w = new BinaryWriter(File.OpenWrite("Sample.bin")))
            //        { BinarySerializer.Write(w, array); }
            //        return "";
            //    },
            //    () =>
            //    {
            //        using (BinaryReader r = new BinaryReader(File.OpenRead("Sample.bin")))
            //        { array = BinarySerializer.ReadArray<long>(r, r.BaseStream.Length); }
            //        return "";
            //    }
            //    );
            
            IndexSet set = new IndexSet(size);
            IndexSet other = new IndexSet(size);

            IndexSet[] sets = new IndexSet[ParallelCount];
            for (int i = 0; i < sets.Length; ++i)
            {
                sets[i] = new IndexSet(size / ParallelCount);
            }

            Span<int> page = new Span<int>(new int[4096]);

            byte[] bucketSample = new byte[size];
            Span<byte> bucketSpan = new Span<byte>(bucketSample);

            ushort[] bigBucketSample = new ushort[size];
            Span<ushort> bigSpan = new Span<ushort>(bigBucketSample);

            Random random = new Random(6);
            random.NextBytes(bucketSample);

            for (int i = 0; i < size; ++i)
            {
                bigBucketSample[i] = (ushort)(random.Next() & ushort.MaxValue);
            }

            //int sum;
            //Benchmark.Compare("Span Operations", iterations, size, new string[] { "Array For", "Array ForEach", "Span For", "Span ForEach" },
            //    () => { sum = 0; for (int i = 0; i < bucketSample.Length; ++i) { sum += bucketSample[i]; } return sum; },
            //    () => { sum = 0; foreach (int item in bucketSample) { sum += item; } return sum; },
            //    () => { sum = 0; for (int i = 0; i < bucketSpan.Length; ++i) { sum += bucketSpan[i]; } return sum; },
            //    () => { sum = 0; foreach (int item in bucketSpan) { sum += item; } return sum; }
            //);

            Benchmark.Compare("IndexSet Operations", iterations, size, new string[] { /*"All", "None", "And", "Count", "WhereGreaterThan",*/ "WhereGreaterThanTwoByte"/*, $"Where Parallel x{ParallelCount}"*/ },
                //() => set.All(size),
                //() => set.None(),
                //() => set.And(other),
                //() => set.Count,
                //() => set.Where(BooleanOperator.Set, bucketSample, CompareOperator.GreaterThan, (byte)200),
                () => set.Where(BooleanOperator.Set, bigBucketSample, CompareOperator.GreaterThan, (ushort)65000)//,
                //() => ParallelWhere(bucketSample, (byte)200, sets)
            );

            //set.None();
            //Benchmark.Compare("IndexSet Page", iterations, size, new string[] { "Page None" }, () => PageAll(set, page));

            //set.None();
            //for (int i = 0; i < set.Capacity; i += 50)
            //{
            //    set[i] = true;
            //}
            //Benchmark.Compare("IndexSet Page", iterations, size, new string[] { "Page 1/50" }, () => PageAll(set, page));

            //set.None();
            //for (int i = 0; i < set.Capacity; i += 10)
            //{
            //    set[i] = true;
            //}
            //Benchmark.Compare("IndexSet Page", iterations, size, new string[] { "Page 1/10" }, () => PageAll(set, page));

            //set.All(size);
            //Benchmark.Compare("IndexSet Page", iterations, size, new string[] { "Page All" }, () => PageAll(set, page));
        }

        private static object ParallelWhere(byte[] column, byte value, IndexSet[] sets)
        {
            Parallel.For(0, sets.Length, (i) =>
            {
                int length = column.Length / sets.Length;
                int offset = i * length;

                sets[i].Where(BooleanOperator.And, column, CompareOperator.GreaterThan, value, offset, length);
            });

            int sum = 0;
            //for(int i = 0; i < sets.Length; ++i)
            //{
            //    sum += sets[i].Count;
            //}
            return sum;
        }

        private static int PageAll(IndexSet set, Span<int> page)
        {
            int count = 0;

            int next = 0;
            while (next != -1)
            {
                next = set.Page(ref page, next);
                count += page.Length;
            }

            return count;
        }

        private static int QueryManagedDirect(V0.WebRequestDatabase db, IndexSet matches)
        {
            for (int i = 0; i < db.Requests.Count; ++i)
            {
                if (db.Requests[i].HttpStatus == 404 && db.Requests[i].ResponseBytes > 1000) matches[i] = true;
            }

            return matches.Count;
        }

        private static int QueryManagedDirect(WebRequestDatabase db, IndexSet matches)
        {
            for (int i = 0; i < db.Count; ++i)
            {
                if (db.HttpStatus.Values[i] == 404 && db.ResponseBytes.Values[i] > 1000) matches[i] = true;
            }

            return matches.Count;
        }

        private static int QueryManagedColumn(WebRequestDatabase db, IndexSet matches, Span<int> page)
        {
            matches.All(db.Count);
            db.HttpStatus.Where(matches, BooleanOperator.And, CompareOperator.Equals, 404);
            db.ResponseBytes.Where(matches, BooleanOperator.And, CompareOperator.GreaterThan, 1000);

            return matches.Count;
        }

        private static int QueryV5(WebRequestDatabase db, IndexSet[] matches, Span<int>[] pages)
        {
            // Look up the buckets for HttpStatus 404 and ResponseBytes 1000
            
            // TODO: We don't need the post-scan for the ResponseBytes column, but we don't realize it.
            // The bucket boundaries are 999 and 1,001, so the 1,001 bucket, while not equal to the query value, is the first in-range value.

            bool isHttpStatusExact;
            int httpStatusBucket = db.HttpStatusBuckets.BucketForValue(404, out isHttpStatusExact);
            bool needHttpStatusPostScan = (isHttpStatusExact == false && db.HttpStatusBuckets.IsMultiValue[httpStatusBucket]);

            bool isResponseBytesExact;
            int responseBytesBucket = db.ResponseBytesBuckets.BucketForValue(1000, out isResponseBytesExact);
            bool needResponseBytesPostScan = (isResponseBytesExact == false && db.ResponseBytesBuckets.IsMultiValue[responseBytesBucket]);

            object locker = new object();
            int total = 0;

            Parallel.For(0, matches.Length, (i) =>
            {
                int length = db.Count / matches.Length;
                int offset = i * length;

                // Get matches in those bucket ranges and intersect them
                matches[i].Where(BooleanOperator.Set, db.HttpStatusBuckets.RowBucketIndex, CompareOperator.Equals, (byte)httpStatusBucket, offset, length);
                matches[i].Where(BooleanOperator.And, db.ResponseBytesBuckets.RowBucketIndex, CompareOperator.GreaterThan, (byte)responseBytesBucket, offset, length);

                // If no post-scans were required, return the bit vector count
                if (!needHttpStatusPostScan && !needResponseBytesPostScan)
                {
                    lock (locker)
                    {
                        total += matches[i].Count;
                    }

                    return;
                }

                // Otherwise, page through results and post-filter on required clauses
                // [NOTE: We should prefer to scan twice and only filter boundary bucket rows when there are many matches]
                int count = 0;
                int matchesBefore = 0;

                int next = 0;
                while (next != -1)
                {
                    next = matches[i].Page(ref pages[i], next);
                    matchesBefore += pages[i].Length;

                    if (needHttpStatusPostScan) db.HttpStatus.Where(ref pages[i], BooleanOperator.And, CompareOperator.Equals, 404, offset);
                    if (needResponseBytesPostScan) db.ResponseBytes.Where(ref pages[i], BooleanOperator.And, CompareOperator.GreaterThan, 1000, offset);

                    count += pages[i].Length;
                }

                lock(locker)
                {
                    total += count;
                }
            });

            // Return the final count
            return total;
        }

        private static void GenerateSampleCsv()
        {
            Random r = new Random(5);
            int rowCount = 1000 * 1000;

            using (ITabularWriter writer = TabularFactory.BuildWriter($"WebRequests.V0.{rowCount}.csv"))
            {
                WebRequestGenerator generator = new WebRequestGenerator(r, DateTime.UtcNow.AddSeconds(-rowCount / 250), 250);
                generator.WriteTo(writer, rowCount);
            }
        }
    }
}

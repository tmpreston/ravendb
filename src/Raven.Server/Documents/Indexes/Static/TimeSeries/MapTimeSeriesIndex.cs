﻿using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Client;
using Raven.Client.Documents.Changes;
using Raven.Client.Documents.Indexes;
using Raven.Server.Config;
using Raven.Server.Documents.Indexes.Configuration;
using Raven.Server.Documents.Indexes.Persistence.Lucene;
using Raven.Server.Documents.Indexes.Workers;
using Raven.Server.Documents.Indexes.Workers.TimeSeries;
using Raven.Server.Documents.Queries;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Documents.Indexes.Static.TimeSeries
{
    public class MapTimeSeriesIndex : MapIndexBase<MapIndexDefinition, IndexField>
    {
        private readonly HashSet<string> _referencedCollections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        protected internal readonly StaticTimeSeriesIndexBase _compiled;

        private HandleReferences _handleReferences;

        protected MapTimeSeriesIndex(MapIndexDefinition definition, StaticTimeSeriesIndexBase compiled)
            : base(definition.IndexDefinition.Type, definition.IndexDefinition.SourceType, definition)
        {
            _compiled = compiled;

            if (_compiled.ReferencedCollections == null)
                return;

            foreach (var collection in _compiled.ReferencedCollections)
            {
                foreach (var referencedCollection in collection.Value)
                    _referencedCollections.Add(referencedCollection.Name);
            }
        }

        protected override void SubscribeToChanges(DocumentDatabase documentDatabase)
        {
            if (documentDatabase == null)
                return;

            if (_referencedCollections.Count > 0)
                documentDatabase.Changes.OnDocumentChange += HandleDocumentChange;

            documentDatabase.Changes.OnTimeSeriesChange += HandleTimeSeriesChange;
        }

        protected override void UnsubscribeFromChanges(DocumentDatabase documentDatabase)
        {
            if (documentDatabase == null)
                return;

            if (_referencedCollections.Count > 0)
                documentDatabase.Changes.OnDocumentChange -= HandleDocumentChange;

            documentDatabase.Changes.OnTimeSeriesChange -= HandleTimeSeriesChange;
        }

        protected override void HandleDocumentChange(DocumentChange change)
        {
            if (_referencedCollections.Contains(change.CollectionName) == false)
                return;

            _mre.Set();
        }

        protected override IIndexingWork[] CreateIndexWorkExecutors()
        {
            var workers = new List<IIndexingWork>
            {
                new CleanupDocuments(this, DocumentDatabase.DocumentsStorage, _indexStorage, Configuration, null)
            };

            if (_referencedCollections.Count > 0)
                workers.Add(_handleReferences = new HandleTimeSeriesReferences(this, _compiled.ReferencedCollections, DocumentDatabase.DocumentsStorage.TimeSeriesStorage, DocumentDatabase.DocumentsStorage, _indexStorage, Configuration));

            workers.Add(new MapTimeSeries(this, DocumentDatabase.DocumentsStorage.TimeSeriesStorage, _indexStorage, null, Configuration));

            return workers.ToArray();
        }

        public override void HandleDelete(Tombstone tombstone, string collection, IndexWriteOperation writer, TransactionOperationContext indexContext, IndexingStatsScope stats)
        {
            //if (_referencedCollections.Count > 0)
            //    _handleReferences.HandleDelete(tombstone, collection, writer, indexContext, stats);

            writer.DeleteBySourceDocument(tombstone.LowerId, stats);
        }

        protected override IndexItem GetItemByEtag(DocumentsOperationContext databaseContext, long etag)
        {
            var timeSeries = DocumentDatabase.DocumentsStorage.TimeSeriesStorage.GetTimeSeries(databaseContext, etag);
            if (timeSeries == null)
                return default;

            return new IndexItem(timeSeries.DocIdAndName, timeSeries.DocIdAndName, timeSeries.DocId, timeSeries.DocId, timeSeries.Etag, timeSeries.Baseline, timeSeries.Name, timeSeries.SegmentSize, timeSeries, IndexItemType.TimeSeries);
        }

        protected override bool IsStale(DocumentsOperationContext databaseContext, TransactionOperationContext indexContext, long? cutoff = null, long? referenceCutoff = null, List<string> stalenessReasons = null)
        {
            var isStale = base.IsStale(databaseContext, indexContext, cutoff, referenceCutoff, stalenessReasons);
            if (isStale && stalenessReasons == null || _referencedCollections.Count == 0)
                return isStale;

            return StaticIndexHelper.IsStaleDueToReferences(this, databaseContext, indexContext, referenceCutoff, stalenessReasons) || isStale;
        }

        public override Dictionary<string, HashSet<CollectionName>> GetReferencedCollections()
        {
            return _compiled.ReferencedCollections;
        }

        protected override unsafe long CalculateIndexEtag(DocumentsOperationContext documentsContext, TransactionOperationContext indexContext,
            QueryMetadata query, bool isStale)
        {
            if (_referencedCollections.Count == 0)
                return base.CalculateIndexEtag(documentsContext, indexContext, query, isStale);

            var minLength = MinimumSizeForCalculateIndexEtagLength(query);
            var length = minLength +
                         sizeof(long) * 2 * (Collections.Count * _referencedCollections.Count); // last referenced collection etags and last processed reference collection etags

            var indexEtagBytes = stackalloc byte[length];

            CalculateIndexEtagInternal(indexEtagBytes, isStale, State, documentsContext, indexContext);
            UseAllDocumentsCounterCmpXchgAndTimeSeriesEtags(documentsContext, query, length, indexEtagBytes);

            var writePos = indexEtagBytes + minLength;

            return StaticIndexHelper.CalculateIndexEtag(this, length, indexEtagBytes, writePos, documentsContext, indexContext);
        }

        public override long GetLastItemEtagInCollection(DocumentsOperationContext databaseContext, string collection)
        {
            if (collection == Constants.Documents.Collections.AllDocumentsCollection)
                throw new InvalidOperationException("TODO ppekrol");

            return DocumentDatabase.DocumentsStorage.TimeSeriesStorage.GetLastTimeSeriesEtag(databaseContext, collection);
        }

        public override (ICollection<string> Static, ICollection<string> Dynamic) GetEntriesFields()
        {
            throw new NotImplementedException();
        }

        public override IIndexedItemEnumerator GetMapEnumerator(IEnumerable<IndexItem> items, string collection, TransactionOperationContext indexContext, IndexingStatsScope stats, IndexType type)
        {
            return new StaticIndexItemEnumerator<DynamicTimeSeriesSegment>(items, _compiled.Maps[collection], collection, stats, type);
        }

        public static Index CreateNew(IndexDefinition definition, DocumentDatabase documentDatabase)
        {
            var instance = CreateIndexInstance(definition, documentDatabase.Configuration);
            instance.Initialize(documentDatabase,
                new SingleIndexConfiguration(definition.Configuration, documentDatabase.Configuration),
                documentDatabase.Configuration.PerformanceHints);

            return instance;
        }

        private static MapTimeSeriesIndex CreateIndexInstance(IndexDefinition definition, RavenConfiguration configuration)
        {
            var staticIndex = (StaticTimeSeriesIndexBase)IndexCompilationCache.GetIndexInstance(definition, configuration);

            var staticMapIndexDefinition = new MapIndexDefinition(definition, staticIndex.Maps.Keys.ToHashSet(), staticIndex.OutputFields, staticIndex.HasDynamicFields);
            var instance = new MapTimeSeriesIndex(staticMapIndexDefinition, staticIndex);
            return instance;
        }

        private void HandleTimeSeriesChange(TimeSeriesChange change)
        {
            if (HandleAllDocs)
                throw new InvalidOperationException("TODO ppekrol");

            if (Collections.Contains(change.CollectionName) == false)
                return;

            _mre.Set();
        }
    }
}
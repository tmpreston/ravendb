﻿using Raven.Server.Documents;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Smuggler.Documents.Iteration
{
    public class TombstonesIterationState : CollectionAwareIterationState<Tombstone>
    {
        public TombstonesIterationState(DocumentsOperationContext context) : base(context)
        {
        }

        public override void OnMoveNext(Tombstone current)
        {
            if (StartEtagByCollection.Count != 0)
            {
                StartEtagByCollection[CurrentCollection] = current.Etag + 1;
            }
            else
            {
                StartEtag = current.Etag + 1;
            }

            ReadCount++;
        }
    }
}
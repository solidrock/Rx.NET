﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace System.Linq
{
    public static partial class AsyncEnumerable
    {
        internal enum AsyncIteratorState
        {
            New = 0,
            Allocated = 1,
            Iterating = 2,
            Disposed = -1,
        }

        internal abstract class AsyncIterator<TSource> : IAsyncEnumerable<TSource>, IAsyncEnumerator<TSource>
        {
            

            private readonly int threadId;
            internal AsyncIteratorState state = AsyncIteratorState.New;
            internal TSource current;
            private CancellationTokenSource cancellationTokenSource;
            private bool currentIsInvalid = true;

            protected AsyncIterator()
            {
                threadId = Environment.CurrentManagedThreadId;
            }

            public abstract AsyncIterator<TSource> Clone();

            public IAsyncEnumerator<TSource> GetEnumerator()
            {
                var enumerator = state == AsyncIteratorState.New && threadId == Environment.CurrentManagedThreadId ? this : Clone();

                enumerator.state = AsyncIteratorState.Allocated;
                enumerator.cancellationTokenSource = new CancellationTokenSource();
                return enumerator;
            }

            
            public virtual void Dispose()
            {
                if (!cancellationTokenSource.IsCancellationRequested)
                {
                    cancellationTokenSource.Cancel();
                }
                cancellationTokenSource.Dispose();

                current = default(TSource);
                state = AsyncIteratorState.Disposed;
            }

            public TSource Current
            {
                get
                {
                    if (currentIsInvalid)
                        throw new InvalidOperationException("Enumerator is in an invalid state");
                    return current;
                }
            }

            public async Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                if (state == AsyncIteratorState.Disposed)
                {
                    return false;
                }

                using (cancellationToken.Register(Dispose))
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancellationTokenSource.Token))
                {
                    try
                    {
                        // Short circuit and don't even call MoveNexCore
                        cancellationToken.ThrowIfCancellationRequested();

                        var result = await MoveNextCore(cts.Token).ConfigureAwait(false);

                        currentIsInvalid = !result; // if move next is false, invalid otherwise valid

                        return result;
                    }
                    catch
                    {
                        currentIsInvalid = true;
                        Dispose();
                        throw;
                    }
                }
            }

            protected abstract Task<bool> MoveNextCore(CancellationToken cancellationToken);

            public virtual IAsyncEnumerable<TResult> Select<TResult>(Func<TSource, TResult> selector)
            {
                return new SelectEnumerableAsyncIterator<TSource, TResult>(this, selector);
            }

            public virtual IAsyncEnumerable<TSource> Where(Func<TSource, bool> predicate)
            {
                return new WhereEnumerableAsyncIterator<TSource>(this, predicate);
            }
        }
    }
}
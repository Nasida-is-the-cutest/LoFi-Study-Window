using System;
using System.Collections;
using System.Collections.Generic;

namespace PotatoOptimization.Utilities
{
    // Unity executes nested enumerators outside the caller's try/catch. Flatten them
    // here so a failed transition always reaches its rollback and finally blocks.
    public static class CoroutineGuard
    {
        public static IEnumerator Run(IEnumerator routine, Action<Exception> onFailure)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            try
            {
                while (stack.Count > 0)
                {
                    var current = stack.Peek();
                    bool moved = false;
                    object value = null;
                    Exception failure = null;
                    try { moved = current.MoveNext(); if (moved) value = current.Current; }
                    catch (Exception error) { failure = error; }
                    if (failure != null) { onFailure?.Invoke(failure); yield break; }
                    if (!moved) { stack.Pop(); (current as IDisposable)?.Dispose(); continue; }
                    if (value is IEnumerator child) stack.Push(child);
                    else yield return value;
                }
            }
            finally
            {
                while (stack.Count > 0)
                {
                    try { (stack.Pop() as IDisposable)?.Dispose(); }
                    catch (Exception error) { onFailure?.Invoke(error); }
                }
            }
        }
    }
}

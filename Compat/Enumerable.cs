// Windows 2000 / .NET 2.0 build only: LINQ to Objects (.NET 3.5 System.Core), rebuilt with the same
// signatures and semantics (deferred execution, stable sorting, exceptions on empty sequences), so the
// program's sources compile unchanged against .NET 2.0. Covers the operators the program uses plus
// their usual siblings.

using System.Collections;
using System.Collections.Generic;

namespace System.Linq
{
    /// <summary>A sorted sequence that further keys can refine (ThenBy).</summary>
    public interface IOrderedEnumerable<TElement> : IEnumerable<TElement>
    {
        /// <summary>Adds a subordinate sort key.</summary>
        IOrderedEnumerable<TElement> CreateOrderedEnumerable<TKey>(Func<TElement, TKey> keySelector, IComparer<TKey> comparer, bool descending);
    }

    /// <summary>A group of elements sharing a key.</summary>
    public interface IGrouping<out TKey, TElement> : IEnumerable<TElement>
    {
        /// <summary>The shared key.</summary>
        TKey Key { get; }
    }

    /// <summary>LINQ to Objects operators.</summary>
    public static class Enumerable
    {
        // ---------------------------------------------------------------- generation

        /// <summary>An empty sequence.</summary>
        public static IEnumerable<TResult> Empty<TResult>() => new TResult[0];

        /// <summary>The integers start .. start + count - 1.</summary>
        public static IEnumerable<int> Range(int start, int count)
        {
            if (count < 0 || (long)start + count - 1 > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            return RangeIterator(start, count);
        }

        /// <summary>The element repeated count times.</summary>
        public static IEnumerable<TResult> Repeat<TResult>(TResult element, int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            return RepeatIterator(element, count);
        }

        // ---------------------------------------------------------------- projection and filtering

        /// <summary>Projects each element.</summary>
        public static IEnumerable<TResult> Select<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector)
        {
            Check(source, selector);
            return SelectIterator(source, selector);
        }

        /// <summary>Projects each element with its index.</summary>
        public static IEnumerable<TResult> Select<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, int, TResult> selector)
        {
            Check(source, selector);
            return SelectIndexIterator(source, selector);
        }

        /// <summary>Projects each element to a sequence and flattens them.</summary>
        public static IEnumerable<TResult> SelectMany<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, IEnumerable<TResult>> selector)
        {
            Check(source, selector);
            return SelectManyIterator(source, selector);
        }

        /// <summary>Projects each element to a sequence, flattens them and combines each inner element with its source.</summary>
        public static IEnumerable<TResult> SelectMany<TSource, TCollection, TResult>(this IEnumerable<TSource> source,
            Func<TSource, IEnumerable<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector)
        {
            Check(source, collectionSelector);
            Check(source, resultSelector);
            return SelectManyResultIterator(source, collectionSelector, resultSelector);
        }

        /// <summary>Keeps the elements matching the predicate.</summary>
        public static IEnumerable<TSource> Where<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            Check(source, predicate);
            return WhereIterator(source, predicate);
        }

        /// <summary>Keeps the elements matching the predicate, which also gets the index.</summary>
        public static IEnumerable<TSource> Where<TSource>(this IEnumerable<TSource> source, Func<TSource, int, bool> predicate)
        {
            Check(source, predicate);
            return WhereIndexIterator(source, predicate);
        }

        /// <summary>Casts every element.</summary>
        public static IEnumerable<TResult> Cast<TResult>(this IEnumerable source)
        {
            Check(source);
            return source as IEnumerable<TResult> ?? CastIterator<TResult>(source);
        }

        /// <summary>Keeps the elements of the given type.</summary>
        public static IEnumerable<TResult> OfType<TResult>(this IEnumerable source)
        {
            Check(source);
            return OfTypeIterator<TResult>(source);
        }

        /// <summary>Returns the sequence typed as IEnumerable.</summary>
        public static IEnumerable<TSource> AsEnumerable<TSource>(this IEnumerable<TSource> source) => source;

        // ---------------------------------------------------------------- partitioning and concatenation

        /// <summary>The first count elements.</summary>
        public static IEnumerable<TSource> Take<TSource>(this IEnumerable<TSource> source, int count)
        {
            Check(source);
            return TakeIterator(source, count);
        }

        /// <summary>All but the first count elements.</summary>
        public static IEnumerable<TSource> Skip<TSource>(this IEnumerable<TSource> source, int count)
        {
            Check(source);
            return SkipIterator(source, count);
        }

        /// <summary>Elements up to the first one failing the predicate.</summary>
        public static IEnumerable<TSource> TakeWhile<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            Check(source, predicate);
            return TakeWhileIterator(source, predicate);
        }

        /// <summary>Elements from the first one failing the predicate on.</summary>
        public static IEnumerable<TSource> SkipWhile<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            Check(source, predicate);
            return SkipWhileIterator(source, predicate);
        }

        /// <summary>Both sequences one after the other.</summary>
        public static IEnumerable<TSource> Concat<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second)
        {
            Check(first);
            Check(second);
            return ConcatIterator(first, second);
        }

        /// <summary>The sequence, or a single default element if it is empty.</summary>
        public static IEnumerable<TSource> DefaultIfEmpty<TSource>(this IEnumerable<TSource> source) => source.DefaultIfEmpty(default(TSource));

        /// <summary>The sequence, or the single given element if it is empty.</summary>
        public static IEnumerable<TSource> DefaultIfEmpty<TSource>(this IEnumerable<TSource> source, TSource defaultValue)
        {
            Check(source);
            return DefaultIfEmptyIterator(source, defaultValue);
        }

        /// <summary>The elements in reverse order.</summary>
        public static IEnumerable<TSource> Reverse<TSource>(this IEnumerable<TSource> source)
        {
            Check(source);
            return ReverseIterator(source);
        }

        /// <summary>Pairs elements of both sequences up to the shorter one's end.</summary>
        public static IEnumerable<TResult> Zip<TFirst, TSecond, TResult>(this IEnumerable<TFirst> first, IEnumerable<TSecond> second,
            Func<TFirst, TSecond, TResult> resultSelector)
        {
            Check(first, resultSelector);
            Check(second);
            return ZipIterator(first, second, resultSelector);
        }

        // ---------------------------------------------------------------- set operations

        /// <summary>The distinct elements, in first-seen order.</summary>
        public static IEnumerable<TSource> Distinct<TSource>(this IEnumerable<TSource> source) => source.Distinct(null);

        /// <summary>The distinct elements under the comparer, in first-seen order.</summary>
        public static IEnumerable<TSource> Distinct<TSource>(this IEnumerable<TSource> source, IEqualityComparer<TSource> comparer)
        {
            Check(source);
            return DistinctIterator(source, comparer);
        }

        /// <summary>The distinct elements of both sequences.</summary>
        public static IEnumerable<TSource> Union<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second) => first.Union(second, null);

        /// <summary>The distinct elements of both sequences under the comparer.</summary>
        public static IEnumerable<TSource> Union<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        {
            Check(first);
            Check(second);
            return DistinctIterator(ConcatIterator(first, second), comparer);
        }

        /// <summary>The distinct elements of the first sequence that also occur in the second.</summary>
        public static IEnumerable<TSource> Intersect<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second) => first.Intersect(second, null);

        /// <summary>The distinct elements of the first sequence that also occur in the second, under the comparer.</summary>
        public static IEnumerable<TSource> Intersect<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        {
            Check(first);
            Check(second);
            return IntersectIterator(first, second, comparer);
        }

        /// <summary>The distinct elements of the first sequence that do not occur in the second.</summary>
        public static IEnumerable<TSource> Except<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second) => first.Except(second, null);

        /// <summary>The distinct elements of the first sequence that do not occur in the second, under the comparer.</summary>
        public static IEnumerable<TSource> Except<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        {
            Check(first);
            Check(second);
            return ExceptIterator(first, second, comparer);
        }

        // ---------------------------------------------------------------- ordering and grouping

        /// <summary>Sorts ascending by key (stable).</summary>
        public static IOrderedEnumerable<TSource> OrderBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            source.OrderBy(keySelector, null);

        /// <summary>Sorts ascending by key under the comparer (stable).</summary>
        public static IOrderedEnumerable<TSource> OrderBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        {
            Check(source, keySelector);
            return new OrderedSequence<TSource>(source, null).CreateOrderedEnumerable(keySelector, comparer, false);
        }

        /// <summary>Sorts descending by key (stable).</summary>
        public static IOrderedEnumerable<TSource> OrderByDescending<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            source.OrderByDescending(keySelector, null);

        /// <summary>Sorts descending by key under the comparer (stable).</summary>
        public static IOrderedEnumerable<TSource> OrderByDescending<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        {
            Check(source, keySelector);
            return new OrderedSequence<TSource>(source, null).CreateOrderedEnumerable(keySelector, comparer, true);
        }

        /// <summary>Adds an ascending subordinate key.</summary>
        public static IOrderedEnumerable<TSource> ThenBy<TSource, TKey>(this IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            source.ThenBy(keySelector, null);

        /// <summary>Adds an ascending subordinate key under the comparer.</summary>
        public static IOrderedEnumerable<TSource> ThenBy<TSource, TKey>(this IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        {
            Check(source, keySelector);
            return source.CreateOrderedEnumerable(keySelector, comparer, false);
        }

        /// <summary>Adds a descending subordinate key.</summary>
        public static IOrderedEnumerable<TSource> ThenByDescending<TSource, TKey>(this IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            source.ThenByDescending(keySelector, null);

        /// <summary>Adds a descending subordinate key under the comparer.</summary>
        public static IOrderedEnumerable<TSource> ThenByDescending<TSource, TKey>(this IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
        {
            Check(source, keySelector);
            return source.CreateOrderedEnumerable(keySelector, comparer, true);
        }

        /// <summary>Groups the elements by key, groups in first-seen order.</summary>
        public static IEnumerable<IGrouping<TKey, TSource>> GroupBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            source.GroupBy(keySelector, x => x, null);

        /// <summary>Groups the elements by key under the comparer.</summary>
        public static IEnumerable<IGrouping<TKey, TSource>> GroupBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            IEqualityComparer<TKey> comparer) =>
            source.GroupBy(keySelector, x => x, comparer);

        /// <summary>Groups projected elements by key.</summary>
        public static IEnumerable<IGrouping<TKey, TElement>> GroupBy<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector) =>
            source.GroupBy(keySelector, elementSelector, null);

        /// <summary>Groups projected elements by key under the comparer.</summary>
        public static IEnumerable<IGrouping<TKey, TElement>> GroupBy<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector, IEqualityComparer<TKey> comparer)
        {
            Check(source, keySelector);
            Check(source, elementSelector);
            return GroupByIterator(source, keySelector, elementSelector, comparer);
        }

        // ---------------------------------------------------------------- conversion

        /// <summary>Copies the elements into a list.</summary>
        public static List<TSource> ToList<TSource>(this IEnumerable<TSource> source)
        {
            Check(source);
            return new List<TSource>(source);
        }

        /// <summary>Copies the elements into an array.</summary>
        public static TSource[] ToArray<TSource>(this IEnumerable<TSource> source)
        {
            Check(source);
            return new List<TSource>(source).ToArray();
        }

        /// <summary>Builds a dictionary from the elements; duplicate keys throw.</summary>
        public static Dictionary<TKey, TSource> ToDictionary<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) =>
            source.ToDictionary(keySelector, x => x, null);

        /// <summary>Builds a dictionary from the elements under the comparer; duplicate keys throw.</summary>
        public static Dictionary<TKey, TSource> ToDictionary<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            IEqualityComparer<TKey> comparer) =>
            source.ToDictionary(keySelector, x => x, comparer);

        /// <summary>Builds a dictionary of projected elements; duplicate keys throw.</summary>
        public static Dictionary<TKey, TElement> ToDictionary<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector) =>
            source.ToDictionary(keySelector, elementSelector, null);

        /// <summary>Builds a dictionary of projected elements under the comparer; duplicate keys throw.</summary>
        public static Dictionary<TKey, TElement> ToDictionary<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector, IEqualityComparer<TKey> comparer)
        {
            Check(source, keySelector);
            Check(source, elementSelector);
            Dictionary<TKey, TElement> result = new Dictionary<TKey, TElement>(comparer);
            foreach (TSource item in source)
            {
                result.Add(keySelector(item), elementSelector(item));
            }

            return result;
        }

        /// <summary>Copies the distinct elements into a set.</summary>
        public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source) => new HashSet<TSource>(source);

        // ---------------------------------------------------------------- element access

        /// <summary>The first element; throws if there is none.</summary>
        public static TSource First<TSource>(this IEnumerable<TSource> source) => source.First(_ => true);

        /// <summary>The first matching element; throws if there is none.</summary>
        public static TSource First<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            if (TryFirst(source, predicate, out TSource found))
            {
                return found;
            }

            throw NoMatch();
        }

        /// <summary>The first element, or the default.</summary>
        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source) => source.FirstOrDefault(_ => true);

        /// <summary>The first matching element, or the default.</summary>
        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            TryFirst(source, predicate, out TSource found);
            return found;
        }

        /// <summary>The last element; throws if there is none.</summary>
        public static TSource Last<TSource>(this IEnumerable<TSource> source) => source.Last(_ => true);

        /// <summary>The last matching element; throws if there is none.</summary>
        public static TSource Last<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            if (TryLast(source, predicate, out TSource found))
            {
                return found;
            }

            throw NoMatch();
        }

        /// <summary>The last element, or the default.</summary>
        public static TSource LastOrDefault<TSource>(this IEnumerable<TSource> source) => source.LastOrDefault(_ => true);

        /// <summary>The last matching element, or the default.</summary>
        public static TSource LastOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            TryLast(source, predicate, out TSource found);
            return found;
        }

        /// <summary>The only element; throws if there is none or more than one.</summary>
        public static TSource Single<TSource>(this IEnumerable<TSource> source) => source.Single(_ => true);

        /// <summary>The only matching element; throws if there is none or more than one.</summary>
        public static TSource Single<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            int count = TrySingle(source, predicate, out TSource found);
            if (count == 1)
            {
                return found;
            }

            throw count == 0 ? NoMatch() : new InvalidOperationException("Sequence contains more than one matching element");
        }

        /// <summary>The only element or the default; throws if there is more than one.</summary>
        public static TSource SingleOrDefault<TSource>(this IEnumerable<TSource> source) => source.SingleOrDefault(_ => true);

        /// <summary>The only matching element or the default; throws if there is more than one.</summary>
        public static TSource SingleOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            if (TrySingle(source, predicate, out TSource found) > 1)
            {
                throw new InvalidOperationException("Sequence contains more than one matching element");
            }

            return found;
        }

        /// <summary>The element at the index; throws if out of range.</summary>
        public static TSource ElementAt<TSource>(this IEnumerable<TSource> source, int index)
        {
            Check(source);
            if (source is IList<TSource> list)
            {
                return list[index];
            }

            if (index >= 0)
            {
                foreach (TSource item in source)
                {
                    if (index-- == 0)
                    {
                        return item;
                    }
                }
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }

        /// <summary>The element at the index, or the default if out of range.</summary>
        public static TSource ElementAtOrDefault<TSource>(this IEnumerable<TSource> source, int index)
        {
            Check(source);
            if (index >= 0)
            {
                foreach (TSource item in source)
                {
                    if (index-- == 0)
                    {
                        return item;
                    }
                }
            }

            return default(TSource);
        }

        // ---------------------------------------------------------------- quantifiers

        /// <summary>True if the sequence has any element.</summary>
        public static bool Any<TSource>(this IEnumerable<TSource> source)
        {
            Check(source);
            using (IEnumerator<TSource> e = source.GetEnumerator())
            {
                return e.MoveNext();
            }
        }

        /// <summary>True if any element matches.</summary>
        public static bool Any<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            Check(source, predicate);
            foreach (TSource item in source)
            {
                if (predicate(item))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if every element matches.</summary>
        public static bool All<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            Check(source, predicate);
            foreach (TSource item in source)
            {
                if (!predicate(item))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True if the sequence contains the value.</summary>
        public static bool Contains<TSource>(this IEnumerable<TSource> source, TSource value)
        {
            Check(source);
            return source is ICollection<TSource> collection ? collection.Contains(value) : source.Contains(value, null);
        }

        /// <summary>True if the sequence contains the value under the comparer.</summary>
        public static bool Contains<TSource>(this IEnumerable<TSource> source, TSource value, IEqualityComparer<TSource> comparer)
        {
            Check(source);
            comparer = comparer ?? EqualityComparer<TSource>.Default;
            foreach (TSource item in source)
            {
                if (comparer.Equals(item, value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if both sequences hold equal elements in the same order.</summary>
        public static bool SequenceEqual<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second) => first.SequenceEqual(second, null);

        /// <summary>True if both sequences hold equal elements in the same order, under the comparer.</summary>
        public static bool SequenceEqual<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        {
            Check(first);
            Check(second);
            comparer = comparer ?? EqualityComparer<TSource>.Default;
            if (first is ICollection<TSource> a && second is ICollection<TSource> b && a.Count != b.Count)
            {
                return false;
            }

            using (IEnumerator<TSource> e1 = first.GetEnumerator())
            using (IEnumerator<TSource> e2 = second.GetEnumerator())
            {
                while (e1.MoveNext())
                {
                    if (!e2.MoveNext() || !comparer.Equals(e1.Current, e2.Current))
                    {
                        return false;
                    }
                }

                return !e2.MoveNext();
            }
        }

        // ---------------------------------------------------------------- aggregation

        /// <summary>Number of elements.</summary>
        public static int Count<TSource>(this IEnumerable<TSource> source)
        {
            Check(source);
            if (source is ICollection<TSource> collection)
            {
                return collection.Count;
            }

            int count = 0;
            using (IEnumerator<TSource> e = source.GetEnumerator())
            {
                while (e.MoveNext())
                {
                    checked { count++; }
                }
            }

            return count;
        }

        /// <summary>Number of matching elements.</summary>
        public static int Count<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            Check(source, predicate);
            int count = 0;
            foreach (TSource item in source)
            {
                if (predicate(item))
                {
                    checked { count++; }
                }
            }

            return count;
        }

        /// <summary>Number of elements as long.</summary>
        public static long LongCount<TSource>(this IEnumerable<TSource> source)
        {
            Check(source);
            long count = 0;
            using (IEnumerator<TSource> e = source.GetEnumerator())
            {
                while (e.MoveNext())
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Folds the elements, starting with the first.</summary>
        public static TSource Aggregate<TSource>(this IEnumerable<TSource> source, Func<TSource, TSource, TSource> func)
        {
            Check(source, func);
            using (IEnumerator<TSource> e = source.GetEnumerator())
            {
                if (!e.MoveNext())
                {
                    throw NoElements();
                }

                TSource result = e.Current;
                while (e.MoveNext())
                {
                    result = func(result, e.Current);
                }

                return result;
            }
        }

        /// <summary>Folds the elements onto a seed.</summary>
        public static TAccumulate Aggregate<TSource, TAccumulate>(this IEnumerable<TSource> source, TAccumulate seed, Func<TAccumulate, TSource, TAccumulate> func)
        {
            Check(source, func);
            TAccumulate result = seed;
            foreach (TSource item in source)
            {
                result = func(result, item);
            }

            return result;
        }

        /// <summary>Folds the elements onto a seed and projects the result.</summary>
        public static TResult Aggregate<TSource, TAccumulate, TResult>(this IEnumerable<TSource> source, TAccumulate seed,
            Func<TAccumulate, TSource, TAccumulate> func, Func<TAccumulate, TResult> resultSelector)
        {
            Check(source, resultSelector);
            return resultSelector(source.Aggregate(seed, func));
        }

        /// <summary>Sum of the values.</summary>
        public static int Sum(this IEnumerable<int> source)
        {
            Check(source);
            int sum = 0;
            foreach (int value in source)
            {
                checked { sum += value; }
            }

            return sum;
        }

        /// <summary>Sum of the values.</summary>
        public static long Sum(this IEnumerable<long> source)
        {
            Check(source);
            long sum = 0;
            foreach (long value in source)
            {
                checked { sum += value; }
            }

            return sum;
        }

        /// <summary>Sum of the values.</summary>
        public static double Sum(this IEnumerable<double> source)
        {
            Check(source);
            double sum = 0;
            foreach (double value in source)
            {
                sum += value;
            }

            return sum;
        }

        /// <summary>Sum of the values.</summary>
        public static decimal Sum(this IEnumerable<decimal> source)
        {
            Check(source);
            decimal sum = 0;
            foreach (decimal value in source)
            {
                sum += value;
            }

            return sum;
        }

        /// <summary>Sum of the projected values.</summary>
        public static int Sum<TSource>(this IEnumerable<TSource> source, Func<TSource, int> selector) => source.Select(selector).Sum();

        /// <summary>Sum of the projected values.</summary>
        public static long Sum<TSource>(this IEnumerable<TSource> source, Func<TSource, long> selector) => source.Select(selector).Sum();

        /// <summary>Sum of the projected values.</summary>
        public static double Sum<TSource>(this IEnumerable<TSource> source, Func<TSource, double> selector) => source.Select(selector).Sum();

        /// <summary>Sum of the projected values.</summary>
        public static decimal Sum<TSource>(this IEnumerable<TSource> source, Func<TSource, decimal> selector) => source.Select(selector).Sum();

        /// <summary>Average of the values; throws on an empty sequence.</summary>
        public static double Average(this IEnumerable<int> source)
        {
            Check(source);
            long sum = 0;
            long count = 0;
            foreach (int value in source)
            {
                checked { sum += value; }
                count++;
            }

            if (count == 0)
            {
                throw NoElements();
            }

            return (double)sum / count;
        }

        /// <summary>Average of the values; throws on an empty sequence.</summary>
        public static double Average(this IEnumerable<double> source)
        {
            Check(source);
            double sum = 0;
            long count = 0;
            foreach (double value in source)
            {
                sum += value;
                count++;
            }

            if (count == 0)
            {
                throw NoElements();
            }

            return sum / count;
        }

        /// <summary>Average of the projected values; throws on an empty sequence.</summary>
        public static double Average<TSource>(this IEnumerable<TSource> source, Func<TSource, int> selector) => source.Select(selector).Average();

        /// <summary>Average of the projected values; throws on an empty sequence.</summary>
        public static double Average<TSource>(this IEnumerable<TSource> source, Func<TSource, double> selector) => source.Select(selector).Average();

        /// <summary>Smallest value; throws on an empty sequence.</summary>
        public static int Min(this IEnumerable<int> source) => Extreme(source, -1);

        /// <summary>Smallest value; throws on an empty sequence.</summary>
        public static long Min(this IEnumerable<long> source) => Extreme(source, -1);

        /// <summary>Smallest value; throws on an empty sequence.</summary>
        public static float Min(this IEnumerable<float> source) => Extreme(source, -1);

        /// <summary>Smallest value; throws on an empty sequence.</summary>
        public static double Min(this IEnumerable<double> source) => Extreme(source, -1);

        /// <summary>Smallest value; throws on an empty sequence.</summary>
        public static decimal Min(this IEnumerable<decimal> source) => Extreme(source, -1);

        /// <summary>Smallest value; nulls are skipped, an empty reference-type sequence gives null.</summary>
        public static TSource Min<TSource>(this IEnumerable<TSource> source) => Extreme(source, -1);

        /// <summary>Smallest projected value; throws on an empty sequence.</summary>
        public static int Min<TSource>(this IEnumerable<TSource> source, Func<TSource, int> selector) => Extreme(source.Select(selector), -1);

        /// <summary>Smallest projected value; throws on an empty sequence.</summary>
        public static long Min<TSource>(this IEnumerable<TSource> source, Func<TSource, long> selector) => Extreme(source.Select(selector), -1);

        /// <summary>Smallest projected value; throws on an empty sequence.</summary>
        public static float Min<TSource>(this IEnumerable<TSource> source, Func<TSource, float> selector) => Extreme(source.Select(selector), -1);

        /// <summary>Smallest projected value; throws on an empty sequence.</summary>
        public static double Min<TSource>(this IEnumerable<TSource> source, Func<TSource, double> selector) => Extreme(source.Select(selector), -1);

        /// <summary>Smallest projected value; throws on an empty sequence.</summary>
        public static decimal Min<TSource>(this IEnumerable<TSource> source, Func<TSource, decimal> selector) => Extreme(source.Select(selector), -1);

        /// <summary>Smallest projected value.</summary>
        public static TResult Min<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector) => Extreme(source.Select(selector), -1);

        /// <summary>Largest value; throws on an empty sequence.</summary>
        public static int Max(this IEnumerable<int> source) => Extreme(source, 1);

        /// <summary>Largest value; throws on an empty sequence.</summary>
        public static long Max(this IEnumerable<long> source) => Extreme(source, 1);

        /// <summary>Largest value; throws on an empty sequence.</summary>
        public static float Max(this IEnumerable<float> source) => Extreme(source, 1);

        /// <summary>Largest value; throws on an empty sequence.</summary>
        public static double Max(this IEnumerable<double> source) => Extreme(source, 1);

        /// <summary>Largest value; throws on an empty sequence.</summary>
        public static decimal Max(this IEnumerable<decimal> source) => Extreme(source, 1);

        /// <summary>Largest value; nulls are skipped, an empty reference-type sequence gives null.</summary>
        public static TSource Max<TSource>(this IEnumerable<TSource> source) => Extreme(source, 1);

        /// <summary>Largest projected value; throws on an empty sequence.</summary>
        public static int Max<TSource>(this IEnumerable<TSource> source, Func<TSource, int> selector) => Extreme(source.Select(selector), 1);

        /// <summary>Largest projected value; throws on an empty sequence.</summary>
        public static long Max<TSource>(this IEnumerable<TSource> source, Func<TSource, long> selector) => Extreme(source.Select(selector), 1);

        /// <summary>Largest projected value; throws on an empty sequence.</summary>
        public static float Max<TSource>(this IEnumerable<TSource> source, Func<TSource, float> selector) => Extreme(source.Select(selector), 1);

        /// <summary>Largest projected value; throws on an empty sequence.</summary>
        public static double Max<TSource>(this IEnumerable<TSource> source, Func<TSource, double> selector) => Extreme(source.Select(selector), 1);

        /// <summary>Largest projected value; throws on an empty sequence.</summary>
        public static decimal Max<TSource>(this IEnumerable<TSource> source, Func<TSource, decimal> selector) => Extreme(source.Select(selector), 1);

        /// <summary>Largest projected value.</summary>
        public static TResult Max<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector) => Extreme(source.Select(selector), 1);

        // ---------------------------------------------------------------- helpers

        private static void Check(object source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
        }

        private static void Check(object source, object func)
        {
            Check(source);
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }
        }

        private static InvalidOperationException NoElements() => new InvalidOperationException("Sequence contains no elements");

        private static InvalidOperationException NoMatch() => new InvalidOperationException("Sequence contains no matching element");

        // Min (sign -1) or Max (sign 1). Like LINQ: nulls are skipped; an empty sequence throws for
        // non-nullable value types and gives null otherwise.
        private static T Extreme<T>(IEnumerable<T> source, int sign)
        {
            Check(source);
            Comparer<T> comparer = Comparer<T>.Default;
            bool nullable = default(T) == null;
            bool any = false;
            T best = default(T);
            foreach (T value in source)
            {
                if (nullable && value == null)
                {
                    continue;
                }

                if (!any || Math.Sign(comparer.Compare(value, best)) == sign)
                {
                    best = value;
                    any = true;
                }
            }

            if (!any && !nullable)
            {
                throw NoElements();
            }

            return best;
        }

        private static bool TryFirst<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate, out TSource found)
        {
            Check(source, predicate);
            foreach (TSource item in source)
            {
                if (predicate(item))
                {
                    found = item;
                    return true;
                }
            }

            found = default(TSource);
            return false;
        }

        private static bool TryLast<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate, out TSource found)
        {
            Check(source, predicate);
            bool any = false;
            found = default(TSource);
            foreach (TSource item in source)
            {
                if (predicate(item))
                {
                    found = item;
                    any = true;
                }
            }

            return any;
        }

        private static int TrySingle<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate, out TSource found)
        {
            Check(source, predicate);
            int count = 0;
            found = default(TSource);
            foreach (TSource item in source)
            {
                if (predicate(item))
                {
                    found = item;
                    if (++count > 1)
                    {
                        found = default(TSource);
                        return count;
                    }
                }
            }

            return count;
        }

        private static IEnumerable<int> RangeIterator(int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return start + i;
            }
        }

        private static IEnumerable<TResult> RepeatIterator<TResult>(TResult element, int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return element;
            }
        }

        private static IEnumerable<TResult> SelectIterator<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, TResult> selector)
        {
            foreach (TSource item in source)
            {
                yield return selector(item);
            }
        }

        private static IEnumerable<TResult> SelectIndexIterator<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, int, TResult> selector)
        {
            int index = 0;
            foreach (TSource item in source)
            {
                yield return selector(item, checked(index++));
            }
        }

        private static IEnumerable<TResult> SelectManyIterator<TSource, TResult>(IEnumerable<TSource> source, Func<TSource, IEnumerable<TResult>> selector)
        {
            foreach (TSource item in source)
            {
                foreach (TResult inner in selector(item))
                {
                    yield return inner;
                }
            }
        }

        private static IEnumerable<TResult> SelectManyResultIterator<TSource, TCollection, TResult>(IEnumerable<TSource> source,
            Func<TSource, IEnumerable<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector)
        {
            foreach (TSource item in source)
            {
                foreach (TCollection inner in collectionSelector(item))
                {
                    yield return resultSelector(item, inner);
                }
            }
        }

        private static IEnumerable<TSource> WhereIterator<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            foreach (TSource item in source)
            {
                if (predicate(item))
                {
                    yield return item;
                }
            }
        }

        private static IEnumerable<TSource> WhereIndexIterator<TSource>(IEnumerable<TSource> source, Func<TSource, int, bool> predicate)
        {
            int index = 0;
            foreach (TSource item in source)
            {
                if (predicate(item, checked(index++)))
                {
                    yield return item;
                }
            }
        }

        private static IEnumerable<TResult> CastIterator<TResult>(IEnumerable source)
        {
            foreach (object item in source)
            {
                yield return (TResult)item;
            }
        }

        private static IEnumerable<TResult> OfTypeIterator<TResult>(IEnumerable source)
        {
            foreach (object item in source)
            {
                if (item is TResult result)
                {
                    yield return result;
                }
            }
        }

        private static IEnumerable<TSource> TakeIterator<TSource>(IEnumerable<TSource> source, int count)
        {
            if (count <= 0)
            {
                yield break;
            }

            foreach (TSource item in source)
            {
                yield return item;
                if (--count == 0)
                {
                    yield break;
                }
            }
        }

        private static IEnumerable<TSource> SkipIterator<TSource>(IEnumerable<TSource> source, int count)
        {
            foreach (TSource item in source)
            {
                if (count > 0)
                {
                    count--;
                    continue;
                }

                yield return item;
            }
        }

        private static IEnumerable<TSource> TakeWhileIterator<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            foreach (TSource item in source)
            {
                if (!predicate(item))
                {
                    yield break;
                }

                yield return item;
            }
        }

        private static IEnumerable<TSource> SkipWhileIterator<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            bool yielding = false;
            foreach (TSource item in source)
            {
                if (!yielding && !predicate(item))
                {
                    yielding = true;
                }

                if (yielding)
                {
                    yield return item;
                }
            }
        }

        private static IEnumerable<TSource> ConcatIterator<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second)
        {
            foreach (TSource item in first)
            {
                yield return item;
            }

            foreach (TSource item in second)
            {
                yield return item;
            }
        }

        private static IEnumerable<TSource> DefaultIfEmptyIterator<TSource>(IEnumerable<TSource> source, TSource defaultValue)
        {
            bool any = false;
            foreach (TSource item in source)
            {
                any = true;
                yield return item;
            }

            if (!any)
            {
                yield return defaultValue;
            }
        }

        private static IEnumerable<TSource> ReverseIterator<TSource>(IEnumerable<TSource> source)
        {
            List<TSource> buffer = new List<TSource>(source);
            for (int i = buffer.Count - 1; i >= 0; i--)
            {
                yield return buffer[i];
            }
        }

        private static IEnumerable<TResult> ZipIterator<TFirst, TSecond, TResult>(IEnumerable<TFirst> first, IEnumerable<TSecond> second,
            Func<TFirst, TSecond, TResult> resultSelector)
        {
            using (IEnumerator<TFirst> e1 = first.GetEnumerator())
            using (IEnumerator<TSecond> e2 = second.GetEnumerator())
            {
                while (e1.MoveNext() && e2.MoveNext())
                {
                    yield return resultSelector(e1.Current, e2.Current);
                }
            }
        }

        private static IEnumerable<TSource> DistinctIterator<TSource>(IEnumerable<TSource> source, IEqualityComparer<TSource> comparer)
        {
            HashSet<TSource> seen = new HashSet<TSource>(comparer);
            foreach (TSource item in source)
            {
                if (seen.Add(item))
                {
                    yield return item;
                }
            }
        }

        private static IEnumerable<TSource> IntersectIterator<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        {
            HashSet<TSource> candidates = new HashSet<TSource>(second, comparer);
            foreach (TSource item in first)
            {
                if (candidates.Remove(item))
                {
                    yield return item;
                }
            }
        }

        private static IEnumerable<TSource> ExceptIterator<TSource>(IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        {
            HashSet<TSource> excluded = new HashSet<TSource>(second, comparer);
            foreach (TSource item in first)
            {
                if (excluded.Add(item))
                {
                    yield return item;
                }
            }
        }

        private static IEnumerable<IGrouping<TKey, TElement>> GroupByIterator<TSource, TKey, TElement>(IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector, IEqualityComparer<TKey> comparer)
        {
            // Dictionary keys must not be null, LINQ groups allow a null key: kept apart.
            Dictionary<TKey, Grouping<TKey, TElement>> byKey = new Dictionary<TKey, Grouping<TKey, TElement>>(comparer ?? EqualityComparer<TKey>.Default);
            List<Grouping<TKey, TElement>> order = new List<Grouping<TKey, TElement>>();
            Grouping<TKey, TElement> nullGroup = null;
            foreach (TSource item in source)
            {
                TKey key = keySelector(item);
                Grouping<TKey, TElement> group;
                if (key == null)
                {
                    group = nullGroup;
                    if (group == null)
                    {
                        group = nullGroup = new Grouping<TKey, TElement>(key);
                        order.Add(group);
                    }
                }
                else if (!byKey.TryGetValue(key, out group))
                {
                    group = new Grouping<TKey, TElement>(key);
                    byKey.Add(key, group);
                    order.Add(group);
                }

                group.Items.Add(elementSelector(item));
            }

            foreach (Grouping<TKey, TElement> group in order)
            {
                yield return group;
            }
        }

        private sealed class Grouping<TKey, TElement> : IGrouping<TKey, TElement>
        {
            public Grouping(TKey key)
            {
                this.Key = key;
            }

            public TKey Key { get; }

            public List<TElement> Items { get; } = new List<TElement>();

            public IEnumerator<TElement> GetEnumerator() => this.Items.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
        }

        // Sorted view: the source is buffered and sorted when enumerated (deferred, like LINQ). Each
        // ThenBy adds a key comparison; ties on all keys keep the source order (stable sort).
        private sealed class OrderedSequence<TElement> : IOrderedEnumerable<TElement>
        {
            private readonly IEnumerable<TElement> source;
            private readonly Comparison<TElement> comparison;

            public OrderedSequence(IEnumerable<TElement> source, Comparison<TElement> comparison)
            {
                this.source = source;
                this.comparison = comparison;
            }

            public IOrderedEnumerable<TElement> CreateOrderedEnumerable<TKey>(Func<TElement, TKey> keySelector, IComparer<TKey> comparer, bool descending)
            {
                IComparer<TKey> keyComparer = comparer ?? Comparer<TKey>.Default;
                Comparison<TElement> previous = this.comparison;
                Comparison<TElement> next = (x, y) =>
                {
                    int result = previous == null ? 0 : previous(x, y);
                    if (result != 0)
                    {
                        return result;
                    }

                    result = keyComparer.Compare(keySelector(x), keySelector(y));
                    return descending ? -result : result;
                };
                return new OrderedSequence<TElement>(this.source, next);
            }

            public IEnumerator<TElement> GetEnumerator()
            {
                List<TElement> items = new List<TElement>(this.source);
                int[] order = new int[items.Count];
                for (int i = 0; i < order.Length; i++)
                {
                    order[i] = i;
                }

                Comparison<TElement> comparison = this.comparison;
                Array.Sort(order, (a, b) =>
                {
                    int result = comparison(items[a], items[b]);
                    return result != 0 ? result : a.CompareTo(b);
                });

                foreach (int index in order)
                {
                    yield return items[index];
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
        }
    }
}

// Windows 2000 / .NET 2.0 build only: HashSet and ISet from .NET 3.5/4.0, with the original semantics.

using System.Collections;
using System.Collections.Generic;

namespace System.Collections.Generic
{
    /// <summary>Set abstraction (.NET 4.0).</summary>
    public interface ISet<T> : ICollection<T>
    {
        /// <summary>Adds the item; false if it was already present.</summary>
        new bool Add(T item);

        /// <summary>Adds every item of <paramref name="other"/>.</summary>
        void UnionWith(IEnumerable<T> other);

        /// <summary>Keeps only items also in <paramref name="other"/>.</summary>
        void IntersectWith(IEnumerable<T> other);

        /// <summary>Removes every item of <paramref name="other"/>.</summary>
        void ExceptWith(IEnumerable<T> other);

        /// <summary>Keeps items present in exactly one of both sets.</summary>
        void SymmetricExceptWith(IEnumerable<T> other);

        /// <summary>True if every item is in <paramref name="other"/>.</summary>
        bool IsSubsetOf(IEnumerable<T> other);

        /// <summary>True if every item of <paramref name="other"/> is in this set.</summary>
        bool IsSupersetOf(IEnumerable<T> other);

        /// <summary>True if this set is a subset of <paramref name="other"/> and smaller.</summary>
        bool IsProperSubsetOf(IEnumerable<T> other);

        /// <summary>True if this set is a superset of <paramref name="other"/> and larger.</summary>
        bool IsProperSupersetOf(IEnumerable<T> other);

        /// <summary>True if both share at least one item.</summary>
        bool Overlaps(IEnumerable<T> other);

        /// <summary>True if both contain the same items.</summary>
        bool SetEquals(IEnumerable<T> other);
    }

    /// <summary>Hash-based set (.NET 3.5), backed by a dictionary.</summary>
    [Serializable]
    public class HashSet<T> : ISet<T>
    {
        private readonly Dictionary<T, bool> items;

        /// <summary>Creates an empty set with the default comparer.</summary>
        public HashSet() : this((IEqualityComparer<T>)null)
        {
        }

        /// <summary>Creates an empty set with the given comparer.</summary>
        public HashSet(IEqualityComparer<T> comparer)
        {
            this.items = new Dictionary<T, bool>(comparer ?? EqualityComparer<T>.Default);
        }

        /// <summary>Creates a set holding the distinct items of <paramref name="collection"/>.</summary>
        public HashSet(IEnumerable<T> collection) : this(collection, null)
        {
        }

        /// <summary>Creates a set holding the distinct items of <paramref name="collection"/> under the given comparer.</summary>
        public HashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer) : this(comparer)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            this.UnionWith(collection);
        }

        /// <summary>Comparer used for the items.</summary>
        public IEqualityComparer<T> Comparer => this.items.Comparer;

        /// <inheritdoc/>
        public int Count => this.items.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public bool Add(T item)
        {
            if (this.items.ContainsKey(item))
            {
                return false;
            }

            this.items.Add(item, true);
            return true;
        }

        void ICollection<T>.Add(T item) => this.Add(item);

        /// <inheritdoc/>
        public bool Remove(T item) => this.items.Remove(item);

        /// <inheritdoc/>
        public bool Contains(T item) => this.items.ContainsKey(item);

        /// <inheritdoc/>
        public void Clear() => this.items.Clear();

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex) => this.items.Keys.CopyTo(array, arrayIndex);

        /// <summary>Copies the items to the start of <paramref name="array"/>.</summary>
        public void CopyTo(T[] array) => this.CopyTo(array, 0);

        /// <summary>Removes every item matching <paramref name="match"/>; returns how many.</summary>
        public int RemoveWhere(Predicate<T> match)
        {
            List<T> doomed = new List<T>();
            foreach (T item in this.items.Keys)
            {
                if (match(item))
                {
                    doomed.Add(item);
                }
            }

            foreach (T item in doomed)
            {
                this.items.Remove(item);
            }

            return doomed.Count;
        }

        /// <inheritdoc/>
        public void UnionWith(IEnumerable<T> other)
        {
            foreach (T item in other)
            {
                this.Add(item);
            }
        }

        /// <inheritdoc/>
        public void IntersectWith(IEnumerable<T> other)
        {
            HashSet<T> keep = this.ToSet(other);
            this.RemoveWhere(item => !keep.Contains(item));
        }

        /// <inheritdoc/>
        public void ExceptWith(IEnumerable<T> other)
        {
            foreach (T item in other)
            {
                this.items.Remove(item);
            }
        }

        /// <inheritdoc/>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            foreach (T item in this.ToSet(other).items.Keys)
            {
                if (!this.items.Remove(item))
                {
                    this.items.Add(item, true);
                }
            }
        }

        /// <inheritdoc/>
        public bool IsSubsetOf(IEnumerable<T> other)
        {
            HashSet<T> set = this.ToSet(other);
            foreach (T item in this.items.Keys)
            {
                if (!set.Contains(item))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public bool IsSupersetOf(IEnumerable<T> other)
        {
            foreach (T item in other)
            {
                if (!this.Contains(item))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            HashSet<T> set = this.ToSet(other);
            return set.Count > this.Count && this.IsSubsetOf(set);
        }

        /// <inheritdoc/>
        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            HashSet<T> set = this.ToSet(other);
            return this.Count > set.Count && this.IsSupersetOf(set);
        }

        /// <inheritdoc/>
        public bool Overlaps(IEnumerable<T> other)
        {
            foreach (T item in other)
            {
                if (this.Contains(item))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public bool SetEquals(IEnumerable<T> other)
        {
            HashSet<T> set = this.ToSet(other);
            return set.Count == this.Count && this.IsSubsetOf(set);
        }

        /// <summary>Enumerates the items.</summary>
        public Dictionary<T, bool>.KeyCollection.Enumerator GetEnumerator() => this.items.Keys.GetEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => this.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        private HashSet<T> ToSet(IEnumerable<T> other) =>
            other is HashSet<T> set && Equals(set.Comparer, this.Comparer) ? set : new HashSet<T>(other, this.Comparer);
    }
}

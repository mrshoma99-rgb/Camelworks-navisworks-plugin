namespace CamelWorks.Core.Identity
{
    /// <summary>
    /// An element as a file recorded it: what it is, and where the writing document had it.
    ///
    /// A key alone is not enough. An <see cref="ElementKey"/> is a hash, and a hash cannot be turned
    /// back into the thing it names: given only a key, a document has one route to the element,
    /// which is to compute the key of every element it holds until one matches. On a federated model
    /// that is the indexing pass, and it is why selecting the elements of a clash read from a file
    /// previously did nothing at all.
    ///
    /// The writing seat can spare the reader that. The clash file is written on a seat with the
    /// model open and the clash engine holding both elements, so it records where they were and the
    /// reading seat looks them up.
    ///
    /// Two records rather than one, because they fail differently. The host's own handle is exact
    /// and immediate where it resolves, and resolves only against a document sharing the tree it was
    /// made against. A path of display names is something any document holding the same tree can
    /// walk, at the cost of a descent. Either may be absent — a file written before this existed
    /// carries neither — and the key is the fallback.
    /// </summary>
    public readonly struct ElementLocator
    {
        /// <summary>Record where an element was.</summary>
        /// <param name="key">The element's key, which is what identity is judged on.</param>
        /// <param name="pathId">The host's own handle, or null.</param>
        /// <param name="treePath">The path of display names, or null.</param>
        public ElementLocator(ElementKey key, string? pathId, string? treePath)
        {
            Key = key;
            PathId = pathId;
            TreePath = treePath;
        }

        /// <summary>The element's key. Identity is this; the rest is only how to reach it.</summary>
        public ElementKey Key { get; }

        /// <summary>The host's own handle for the element, or null. Opaque outside the adapter.</summary>
        public string? PathId { get; }

        /// <summary>The element's path down the selection tree, or null.</summary>
        public string? TreePath { get; }

        /// <summary>True when this carries a way to reach the element that costs no search.</summary>
        public bool HasRoute => !string.IsNullOrEmpty(PathId) || !string.IsNullOrEmpty(TreePath);

        /// <inheritdoc />
        public override string ToString() =>
            Key.ToString() + (HasRoute ? " (located)" : " (key only)");
    }
}

using System.Windows;

namespace CamelWorks.UI.Themes
{
    /// <summary>
    /// The compiled skin, as a type rather than a pack URI.
    ///
    /// A resource dictionary can be reached two ways: by <c>pack://application:,,,/…</c>, or by
    /// giving the XAML an <c>x:Class</c> and constructing it. The second is used here because a
    /// plug-in has no <see cref="System.Windows.Application"/> of its own — Navisworks does — and
    /// pack URIs written against an absent application are the kind of thing that works on the
    /// machine it was written on and throws on somebody else's. Constructing a type cannot fail
    /// that way, and the XAML is compiled, so a typo in it is a build error rather than a pane
    /// that comes up unstyled six weeks later.
    /// </summary>
    public partial class SkinSheet : ResourceDictionary
    {
        /// <summary>Load the skin.</summary>
        public SkinSheet() => InitializeComponent();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Compare;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Builders for the compare tests.
    ///
    /// Fingerprints are built directly rather than walked out of a fake document, because what
    /// these tests are about is the matching, and going through a document would mean every case
    /// also depends on how the document assigns rungs.
    /// </summary>
    internal static class CompareFixtures
    {
        internal const string ScopeA = "aaaaaaaa";
        internal const string ScopeB = "bbbbbbbb";

        internal static ElementFingerprint Print(
            string name,
            double x = 0, double y = 0, double z = 0,
            double sx = 1, double sy = 1, double sz = 1,
            string scope = ScopeA,
            KeyRung rung = KeyRung.InstanceGuid,
            string? local = null,
            string? type = "Type",
            string? category = "Category",
            bool hasGeometry = true)
        {
            var value = local ?? Value(name);

            return new ElementFingerprint(rung, value, Value(scope + "/" + value), scope)
            {
                Name = name,
                Type = type ?? string.Empty,
                Category = category ?? string.Empty,
                CentreX = x, CentreY = y, CentreZ = z,
                SizeX = sx, SizeY = sy, SizeZ = sz,
                HasGeometry = hasGeometry,
            };
        }

        /// <summary>A copy with the same identity, so a test can change one thing about it.</summary>
        internal static ElementFingerprint Copy(ElementFingerprint from)
        {
            var copy = new ElementFingerprint(from.Rung, from.Local, from.Scoped, from.ModelScope)
            {
                Name = from.Name,
                Type = from.Type,
                Category = from.Category,
                CentreX = from.CentreX, CentreY = from.CentreY, CentreZ = from.CentreZ,
                SizeX = from.SizeX, SizeY = from.SizeY, SizeZ = from.SizeZ,
                HasGeometry = from.HasGeometry,
                MeshHash = from.MeshHash,
            };

            copy.Values = from.Values.ToList();
            return copy;
        }

        internal static ElementFingerprint With(this ElementFingerprint print, string category,
                                                string name, string value)
        {
            var values = print.Values.ToList();
            values.Add(new WatchedValue(new PropertyRef(category, name), value));
            print.Values = values;
            return print;
        }

        internal static ModelSnapshot Snapshot(string label, IEnumerable<ElementFingerprint> prints,
                                               params ModelRecord[] models)
        {
            var snapshot = new ModelSnapshot
            {
                Label = label,
                TakenUtc = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
                Watch = PropertyWatch.Standard,
            };

            snapshot.Models = models.Length > 0
                ? models
                : new[] { new ModelRecord(ScopeA, "Model", @"C:\job\model.nwc") };

            snapshot.AddRange(prints);
            return snapshot;
        }

        internal static ModelRecord Model(string scope, string name, string path) =>
            new ModelRecord(scope, name, path);

        /// <summary>Sixteen hex characters, derived, so a rebuilt key parses.</summary>
        internal static string Value(string seed) => Hash.Of(Hash.ComponentWidth, "fixture", seed);

        internal static int Count(this CompareResult result, ChangeBucket bucket) => result.CountIn(bucket);

        internal static ElementChange? Find(this CompareResult result, string name) =>
            result.Changes.FirstOrDefault(c => c.Print.Name == name);
    }
}

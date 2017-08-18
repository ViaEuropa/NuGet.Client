// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.ProjectManagement.Projects;
using NuGet.Versioning;

namespace NuGet.PackageManagement.VisualStudio
{
    /// <summary>
    /// Wrapper class consolidating common queries against a collection of packages
    /// </summary>
    public sealed class PackageCollection : IEnumerable<PackageCollectionItem>
    {
        private readonly PackageCollectionItem[] _packages;
        private readonly ISet<string> _uniqueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public PackageCollection(PackageCollectionItem[] packages)
        {
            _packages = packages;
            _uniqueIds.UnionWith(_packages.Select(p => p.Id));
        }

        public IEnumerator<PackageCollectionItem> GetEnumerator()
        {
            return ((IEnumerable<PackageCollectionItem>)_packages).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<PackageCollectionItem>)_packages).GetEnumerator();
        }

        public bool ContainsId(string packageId) => _uniqueIds.Contains(packageId);

        private static async Task<IEnumerable<PackageReference>> GetPackagesWithResolvedVersionIfAvailableAsync(NuGetProject project, Dictionary<string, NuGetVersion> dependencyResolvedVersionLookup, CancellationToken cancellationToken)
        {
            var packages = await project.GetInstalledPackagesAsync(cancellationToken);

            // Only resolve versions if project is build integrated and restore has run
            if (project is BuildIntegratedNuGetProject && dependencyResolvedVersionLookup != null && dependencyResolvedVersionLookup.Any())
            {
                // We need to update the direct dependencies with the version they actually resolved to
                // when project ran restore.

                var resolvedPackages = new List<PackageReference>();
                //  Update the identity of the dependencies to use the actual resolved version
                foreach (var package in packages)
                {
                    // Update the dependency identity to the actual target dependency version
                    var identity = new PackageIdentity(package.PackageIdentity.Id, dependencyResolvedVersionLookup[package.PackageIdentity.Id]);
                    resolvedPackages.Add(PackageReference.CloneWithNewIdentity(package, identity));
                }
                return resolvedPackages;
            }
            // If restore hasn't run then fallback to data from package references
            return packages;
        }

        private static Dictionary<string, NuGetVersion> TryGetProjectDependencyLookup(Dictionary<string, Dictionary<string, NuGetVersion>> dependencyVersionLookup, NuGetProject project)
        {
            if (dependencyVersionLookup != null && dependencyVersionLookup.Any())
            {
                return dependencyVersionLookup[NuGetProject.GetUniqueNameOrName(project)];
            }
            return null;
        }

        public static async Task<PackageCollection> FromProjectsAsync(IEnumerable<NuGetProject> projects, Dictionary<string, Dictionary<string, NuGetVersion>> dependencyVersionLookup, CancellationToken cancellationToken)
        {
            // Read package references from all projects.
            var tasks = projects
                .Select(project => GetPackagesWithResolvedVersionIfAvailableAsync(project, TryGetProjectDependencyLookup(dependencyVersionLookup, project), cancellationToken));

            var packageReferences = await Task.WhenAll(tasks);

            // Group all package references for an id/version into a single item.
            var packages = packageReferences
                    .SelectMany(e => e)
                    .GroupBy(e => e.PackageIdentity, (key, group) => new PackageCollectionItem(key.Id, key.Version, group))
                    .ToArray();

            return new PackageCollection(packages);
        }
    }
}

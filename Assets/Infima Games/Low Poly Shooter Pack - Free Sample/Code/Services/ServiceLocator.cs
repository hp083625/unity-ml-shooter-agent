// Copyright 2021, Infima Games. All Rights Reserved.
//Implementation from: https://medium.com/medialesson/simple-service-locator-for-your-unity-project-40e317aad307

using System;
using System.Collections.Generic;
using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Simple service locator for <see cref="IGameService"/> instances.
    /// </summary>
    public class ServiceLocator
    {
        /// <summary>
        /// Currently registered services.
        /// </summary>
        private readonly Dictionary<string, IGameService> services = new Dictionary<string, IGameService>();

        public static ServiceLocator Current { get; private set; }

        public static void Initialize() { Current = new ServiceLocator(); }

        /// <summary>
        /// Returns the most specific <see cref="ServiceLocator"/> for the given
        /// component. Walks <c>c.transform.parent</c> upward looking for an
        /// <see cref="AreaServiceLocator"/>; if found, returns that area's
        /// private locator. Otherwise (no parent <see cref="AreaServiceLocator"/>,
        /// or <paramref name="c"/> is <c>null</c>), returns <see cref="Current"/>.
        /// </summary>
        /// <remarks>
        /// Per ADR-0006, this enables per-area service overrides for parallel
        /// ML training scenes while preserving the existing global behavior for
        /// any consumer that does not opt-in via <c>ServiceLocator.For(this)</c>.
        /// </remarks>
        /// <param name="c">The component whose scope should be resolved.</param>
        /// <returns>
        /// The area-scoped <see cref="ServiceLocator"/> if one is found by
        /// walking the parent transform chain; otherwise <see cref="Current"/>.
        /// </returns>
        public static ServiceLocator For(Component c)
        {
            //Null caller falls back to the global locator.
            if (c == null)
                return Current;

            //Walk up the parent chain looking for an AreaServiceLocator.
            //We start from c.transform.parent because the issue body specifies
            //"walks c.transform.parent upward" — an AreaServiceLocator on c
            //itself is not in scope here (it would not be a parent).
            Transform t = c.transform.parent;
            while (t != null)
            {
                AreaServiceLocator area = t.GetComponent<AreaServiceLocator>();
                if (area != null && area.Locator != null)
                    return area.Locator;

                t = t.parent;
            }

            //No area scope found — fall through to the global.
            return Current;
        }

        /// <summary>
        /// Gets the service instance of the given type.
        /// </summary>
        /// <typeparam name="T">The type of the service to lookup.</typeparam>
        /// <returns>The service instance.</returns>
        public T Get<T>() where T : IGameService
        {
            string key = typeof(T).Name;
            if (!services.ContainsKey(key))
            {
                Log.kill($"{key} not registered with {GetType().Name}");
                throw new InvalidOperationException();
            }

            return (T)services[key];
        }

        /// <summary>
        /// Registers the service with the current service locator.
        /// </summary>
        /// <typeparam name="T">Service type.</typeparam>
        /// <param name="service">Service instance.</param>
        public void Register<T>(T service) where T : IGameService
        {
            string key = typeof(T).Name;
            if (services.ContainsKey(key))
            {
                Log.kill($"Attempted to register service of type {key} which is already registered with the {GetType().Name}.");
                return;
            }

            //Add.
            services.Add(key, service);
        }

        /// <summary>
        /// Unregisters the service from the current service locator.
        /// </summary>
        /// <typeparam name="T">Service type.</typeparam>
        public void Unregister<T>() where T : IGameService
        {
            string key = typeof(T).Name;
            if (!services.ContainsKey(key))
            {
                Log.kill($"Attempted to unregister service of type {key} which is not registered with the {GetType().Name}.");
                return;
            }

            services.Remove(key);
        }
    }
}

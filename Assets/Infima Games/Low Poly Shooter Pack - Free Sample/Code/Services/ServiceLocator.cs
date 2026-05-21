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
        /// Returns the area-scoped <see cref="ServiceLocator"/> for the given caller, walking up the
        /// caller's transform parent chain looking for an <see cref="AreaServiceLocator"/>. If one is
        /// found, its private locator is returned. Otherwise (no ancestor has one, or <paramref name="caller"/>
        /// is null), the global <see cref="Current"/> locator is returned.
        /// </summary>
        /// <remarks>
        /// Per ADR-0006: lets parallel training areas (each with their own <see cref="AreaServiceLocator"/>
        /// at the prefab root) coexist in one scene with isolated services, while non-training scenes that
        /// have no <c>AreaServiceLocator</c> ancestor continue to resolve through <see cref="Current"/>
        /// unchanged.
        /// </remarks>
        /// <param name="caller">The MonoBehaviour requesting the locator. May be null.</param>
        /// <returns>The nearest area locator on the parent chain, or <see cref="Current"/> as a fallback.</returns>
        public static ServiceLocator For(MonoBehaviour caller)
        {
            if (caller == null)
                return Current;

            //Walk up the transform parent chain looking for an AreaServiceLocator.
            for (Transform t = caller.transform; t != null; t = t.parent)
            {
                AreaServiceLocator area = t.GetComponent<AreaServiceLocator>();
                if (area != null && area.Locator != null)
                    return area.Locator;
            }

            //None found — fall back to the global locator.
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
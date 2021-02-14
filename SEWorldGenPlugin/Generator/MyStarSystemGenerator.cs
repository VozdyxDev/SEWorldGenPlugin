﻿using Sandbox.Definitions;
using Sandbox.Game.World;
using SEWorldGenPlugin.Generator.AsteroidObjects;
using SEWorldGenPlugin.Generator.AsteroidObjects.AsteroidRing;
using SEWorldGenPlugin.Generator.AsteroidObjectShapes;
using SEWorldGenPlugin.Networking.Attributes;
using SEWorldGenPlugin.ObjectBuilders;
using SEWorldGenPlugin.Session;
using SEWorldGenPlugin.Utilities;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Library.Utils;
using VRageMath;

namespace SEWorldGenPlugin.Generator
{
    /// <summary>
    /// Session component that generates the solar system data for the solar system of
    /// the current game session / world, and provides networking functions to
    /// manipulate it clientside. Is a singleton class
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate, 600)]
    [EventOwner]
    public partial class MyStarSystemGenerator : MySessionComponentBase
    {
        /// <summary>
        /// The max amount of tries it should take, to find an appropriate definition for
        /// a planet or moon.
        /// </summary>
        private readonly int MAX_DEF_FIND_ROUNDS = 10000;

        /// <summary>
        /// File name for the file containing the system data for the world
        /// </summary>
        private readonly string STORAGE_FILE = "SolarSystem.xml";

        /// <summary>
        /// Colors used for persistent gps generation
        /// </summary>
        private readonly Color PLANET_GPS_COLOR = Color.White;
        private readonly Color MOON_GPS_COLOR = Color.White;
        private readonly Color RING_GPS_COLOR = Color.Yellow;

        /// <summary>
        /// List of all vanilla planets, to allow users to exclude them from world generation
        /// </summary>
        private readonly List<string> VANILLA_PLANETS = new List<string> { "Alien", "EarthLike", "EarthLikeTutorial", "Europa", "Mars", "MarsTutorial", "Moon", "MoonTutorial", "Pertam", "Titan", "Triton" };

        public static MyStarSystemGenerator Static;

        /// <summary>
        /// All celestial bodies present in the solar system
        /// </summary>
        public MyObjectBuilder_SystemData StarSystem;

        private List<MyPlanetGeneratorDefinition> m_planets;

        private List<MyPlanetGeneratorDefinition> m_suns;

        private List<MyPlanetGeneratorDefinition> m_gasGiants;

        private List<MyPlanetGeneratorDefinition> m_moons;

        private List<MyPlanetGeneratorDefinition> m_mandatoryPlanets;

        /// <summary>
        /// Initializes the system generator and generates a new system if enabled and no system was
        /// generated yet.
        /// </summary>
        /// <param name="sessionComponent"></param>
        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            if (!MySettingsSession.Static.IsEnabled()) return;

            if(StarSystem.Count() <= 0)
            {
                StarSystem = GenerateNewStarSystem();
            }

            AddAllPersistentGps();
        }

        /// <summary>
        /// Loads the data for the system generator and filters the definition according to
        /// the global config
        /// </summary>
        public override void LoadData()
        {
            Static = this;

            LoadNetworking();

            if (!MySettingsSession.Static.IsEnabled()) return;

            MyPluginLog.Log("Loading definitions and network data");
            var data = LoadSystemData();

            StarSystem = data;

            if(StarSystem.CenterObject != null)
            {
                foreach (var obj in StarSystem.GetAllObjects())
                {
                    if (obj.Type == MySystemObjectType.ASTEROIDS)
                    {
                        var asteroid = obj as MySystemAsteroids;

                        var provider = MyAsteroidObjectsManager.Static.AsteroidObjectProviders[asteroid.AsteroidTypeName];
                        provider.TryLoadObject(asteroid);
                    }
                }
            }

            m_planets = new List<MyPlanetGeneratorDefinition>();
            m_moons = new List<MyPlanetGeneratorDefinition>();
            m_suns = new List<MyPlanetGeneratorDefinition>();
            m_gasGiants = new List<MyPlanetGeneratorDefinition>();
            m_mandatoryPlanets = new List<MyPlanetGeneratorDefinition>();

            LoadPlanetDefinitions();
        }

        /// <summary>
        /// Saves the system objects to a file, if the plugin is enabled.
        /// </summary>
        public override void SaveData()
        {
            if (MySettingsSession.Static.IsEnabled())
            {
                MyPluginLog.Log("Saving system data");

                MyFileUtils.WriteXmlFileToWorld(StarSystem, STORAGE_FILE, typeof(MyStarSystemGenerator));
            }
        }

        /// <summary>
        /// Unloads all used data by this component
        /// </summary>
        protected override void UnloadData()
        {
            MyPluginLog.Log("Unloading star system generation data");

            SaveData();
            StarSystem = null;
            m_planets.Clear();
            m_suns.Clear();
            m_gasGiants.Clear();
            m_moons.Clear();
            m_mandatoryPlanets.Clear();

            UnloadNetworking();

            Static = null;
        }

        /// <summary>
        /// Creates all GPSs that are persistent.
        /// </summary>
        private void AddAllPersistentGps()
        {
            var settings = MySettingsSession.Static.Settings.GeneratorSettings.GPSSettings;

            MySession.Static.OnReady += delegate
            {
                foreach(var item in StarSystem.GetAllObjects())
                {
                    switch (item.Type)
                    {
                        case MySystemObjectType.MOON:
                            if (settings.MoonGPSMode == MyGPSGenerationMode.PERSISTENT)
                            {
                                MyGPSManager.Static.AddPersistentGps(item.DisplayName, MOON_GPS_COLOR, item.CenterPosition);
                            }
                            break;
                        case MySystemObjectType.PLANET:
                            if (settings.PlanetGPSMode == MyGPSGenerationMode.PERSISTENT)
                            {
                                MyGPSManager.Static.AddPersistentGps(item.DisplayName, PLANET_GPS_COLOR, item.CenterPosition);
                            }
                            break;
                        case MySystemObjectType.ASTEROIDS:
                            if (settings.AsteroidGPSMode == MyGPSGenerationMode.PERSISTENT)
                            {
                                MySystemAsteroids asteroid = item as MySystemAsteroids;
                                MyAbstractAsteroidObjectProvider provider = null;
                                if (MyAsteroidObjectsManager.Static.AsteroidObjectProviders.TryGetValue(asteroid.AsteroidTypeName, out provider))
                                {
                                    MyGPSManager.Static.AddPersistentGps(item.DisplayName, RING_GPS_COLOR, provider.GetAsteroidObjectShape(asteroid).GetPointInShape());
                                }
                            }
                            break;
                    }
                }
            };
        }

        /// <summary>
        /// Generates a completely new system based on the
        /// world settings.
        /// </summary>
        /// <returns></returns>
        private MyObjectBuilder_SystemData GenerateNewStarSystem()
        {
            MyPluginLog.Log("Generating a new Solar system ...");

            int seed = MySession.Static.Settings.ProceduralSeed + Guid.NewGuid().GetHashCode();
            MyObjectBuilder_SystemData system = new MyObjectBuilder_SystemData();

            var settings = MySettingsSession.Static.Settings.GeneratorSettings;

            var orbitDistances = settings.MinMaxOrbitDistance;
            var planetsAmount = settings.MinMaxPlanets;
            var asteroidObjectAmount = settings.MinMaxAsteroidObjects;
            var worldSize = settings.WorldSize;

            var asteroidProviders = MyAsteroidObjectsManager.Static.AsteroidObjectProviders;

            using (MyRandom.Instance.PushSeed(seed))
            {
                int planetCount = MyRandom.Instance.Next(planetsAmount.Min, planetsAmount.Max + 1);
                int asteroidObjectCount = MyRandom.Instance.Next(asteroidObjectAmount.Min, asteroidObjectAmount.Max + 1);
                int systemSize = planetCount + asteroidObjectCount;
                int currentPlanetIndex = 0;
                int currentAsteroidIndex = 0;
                long currentOrbitDistance = 0;

                double planetProb = planetCount / (double)(planetCount + asteroidObjectCount);

                if(m_suns.Count > 0)
                {
                    MyPlanetGeneratorDefinition sunDef = m_suns[MyRandom.Instance.Next(0, m_suns.Count)];
                    MySystemPlanet sun = new MySystemPlanet();
                    sun.CenterPosition = Vector3D.Zero;
                    sun.SubtypeId = sunDef.Id.SubtypeId.String;
                    sun.DisplayName = sunDef.DisplayNameText;
                    sun.Diameter = CalculatePlanetDiameter(sunDef) * 2;
                    sun.ChildObjects = new HashSet<MySystemObject>();
                    sun.Generated = false;
                    sun.Type = MySystemObjectType.PLANET;

                    system.CenterObject = sun;
                    currentOrbitDistance += (long)sun.Diameter;
                }
                else
                {
                    system.CenterObject = new MySystemObject();
                    system.CenterObject.Type = MySystemObjectType.EMPTY;
                    system.CenterObject.DisplayName = "System center";
                }

                while(planetCount > 0 || asteroidObjectCount > 0)
                {
                    currentOrbitDistance += MyRandom.Instance.Next(orbitDistances.Min, orbitDistances.Max);

                    //Maybe rework to override orbit distance, so all objects fit
                    if (worldSize >= 0 && currentOrbitDistance >= worldSize) return system;

                    MySystemObject obj = null;

                    if (asteroidObjectCount <= 0 || (MyRandom.Instance.NextDouble() <= planetProb && planetCount > 0)) // Generate planet
                    {
                        obj = GeneratePlanet(currentPlanetIndex++, Math.Sin((system.Count() - 1) * Math.PI / systemSize), currentOrbitDistance);
                        planetCount--;
                    }
                    else if (asteroidObjectCount > 0) // Generate asteroid object
                    {
                        int providerIndex = MyRandom.Instance.Next(0, asteroidProviders.Keys.Count);
                        MyAbstractAsteroidObjectProvider provider = null;
                        foreach(var prov in asteroidProviders)
                        {
                            if (!prov.Value.IsSystemGeneratable()) continue;

                            if(providerIndex-- == 0)
                            {
                                provider = prov.Value;
                            }
                        }

                        if (provider == null) continue;

                        obj = provider.GenerateInstance(currentAsteroidIndex++, null, currentOrbitDistance);
                        (obj as MySystemAsteroids).AsteroidTypeName = provider.GetTypeName();

                        asteroidObjectCount--;
                    }
                    if (obj == null) continue;

                    obj.ParentName = system.CenterObject.DisplayName;
                    system.CenterObject.ChildObjects.Add(obj);
                }
            }

            MyPluginLog.Log("Solar system generated ...");

            return system;
        }

        /// <summary>
        /// Generates a planet for the star system.
        /// </summary>
        /// <param name="planetIndex">Index of the planet in the system</param>
        /// <param name="maxDiameter">The largest diameter the planet should have</param>
        /// <param name="orbitDistance">The distance the planet is away from Vector3D.Zero</param>
        /// <returns>A new MySystemPlanet</returns>
        private MySystemPlanet GeneratePlanet(int planetIndex, double maxDiameter, long orbitDistance)
        {
            MyPluginLog.Debug("Generating new planet");
            var def = FindPlanetDefinitionForSize(maxDiameter);
            var diameter = CalculatePlanetDiameter(def);

            var angle = MyRandom.Instance.GetRandomFloat(0, (float)(2 * Math.PI));
            var elevation = MyRandom.Instance.GetRandomFloat((float)Math.PI / 180 * -5, (float)Math.PI / 180 * 5);
            Vector3D pos = new Vector3D(orbitDistance * Math.Sin(angle), orbitDistance * Math.Cos(angle), orbitDistance * Math.Sin(elevation));

            string name = GetPlanetName(planetIndex, def.Id.SubtypeId.String);

            MySystemPlanet planet = new MySystemPlanet()
            {
                CenterPosition = pos,
                Diameter = diameter,
                DisplayName = name,
                Generated = false,
                SubtypeId = def.Id.SubtypeId.String,
            };

            if(MyRandom.Instance.NextFloat() > 0.25f * def.SurfaceGravity)
            {
                planet.ChildObjects.Add(GenrateRing(planet));
            }

            if (MyRandom.Instance.NextFloat() > 0.3f * def.SurfaceGravity)
            {
                foreach(var moon in GeneratePlanetMoons(planet))
                {
                    if (moon == null) continue;
                    planet.ChildObjects.Add(moon);
                }
            }
            MyPluginLog.Debug("Planet generated");
            return planet;
        }

        /// <summary>
        /// Generates a random amount of moons for a planet
        /// </summary>
        /// <param name="parentPlanet"></param>
        /// <returns></returns>
        private MySystemPlanetMoon[] GeneratePlanetMoons(MySystemPlanet parentPlanet)
        {
            MyPluginLog.Debug("Generating moons for planet " + parentPlanet.DisplayName);
            var settings = MySettingsSession.Static.Settings.GeneratorSettings;

            int maxMoons = (int)Math.Ceiling(parentPlanet.Diameter / 120 * 2);
            int numMoons = MyRandom.Instance.Next(1, maxMoons + 1);

            if(settings.SystemGenerator == SystemGenerationMethod.UNIQUE)
            {
                numMoons = Math.Min(numMoons, m_moons.Count);
            }

            MySystemPlanetMoon[] moons = new MySystemPlanetMoon[numMoons];

            for(int i = 0; i < numMoons; i++)
            {
                double distance = parentPlanet.Diameter * (i + 1) + parentPlanet.Diameter * MyRandom.Instance.GetRandomFloat(0.5f, 1.5f);
                var definition = FindMoonDefinitinon(parentPlanet.Diameter);
                if (definition == null) return moons;

                double diameter = CalculatePlanetDiameter(definition);

                Vector3D position;

                int tries2 = 0;

                do
                {
                    double angle = MyRandom.Instance.GetRandomFloat(0, (float)Math.PI * 2f);
                    position = new Vector3D(distance * Math.Sin(angle), distance * Math.Cos(angle), distance * Math.Sin(MyRandom.Instance.GetRandomFloat((float)-Math.PI / 2, (float)Math.PI / 2)));
                    position = Vector3D.Add(position, parentPlanet.CenterPosition);

                } while (IsMoonPositionObstructed(position, diameter, parentPlanet, moons) && tries2 < MAX_DEF_FIND_ROUNDS);

                MySystemPlanetMoon moon = new MySystemPlanetMoon();
                moon.CenterPosition = position;
                moon.Diameter = diameter;
                moon.DisplayName = GetMoonName(i, definition.Id.SubtypeId.String, parentPlanet.DisplayName);
                moon.SubtypeId = definition.Id.SubtypeId.String;
                moon.ParentName = parentPlanet.DisplayName;

                moons[i] = moon;
            }

            return moons;
        }

        /// <summary>
        /// Generates a ring for the given planet
        /// </summary>
        /// <param name="parentPlanet">Planet the ring is based on</param>
        /// <returns>The ring for the planet</returns>
        private MySystemAsteroids GenrateRing(MySystemPlanet parentPlanet)
        {
            MyPluginLog.Debug("Generating ring for planet " + parentPlanet.DisplayName);

            var provider = MyAsteroidObjectsManager.Static.AsteroidObjectProviders[MyAsteroidRingProvider.TYPE_NAME];
            var ring = provider.GenerateInstance(0, parentPlanet, 0);

            return ring;
        }

        /// <summary>
        /// Checks, whether a given position, where a moon with given diameter is located, is obstructed by moons or a planet or its ring.
        /// Helper method for GeneratePlanetMoons
        /// </summary>
        /// <param name="position">Position to check</param>
        /// <param name="moonDiameter">Diameter of moon</param>
        /// <param name="parentPlanet">Planet to check ring</param>
        /// <param name="moons">Moons to check</param>
        /// <returns></returns>
        private bool IsMoonPositionObstructed(Vector3D position, double moonDiameter, MySystemPlanet parentPlanet, MySystemPlanetMoon[] moons)
        {
            if (Vector3D.Distance(position, parentPlanet.CenterPosition) < moonDiameter + parentPlanet.Diameter) return true;

            foreach(var child in parentPlanet.ChildObjects)
            {
                if(child.Type == MySystemObjectType.ASTEROIDS)
                {
                    var asteroid = child as MySystemAsteroids;
                    IMyAsteroidObjectShape shape = MyAsteroidObjectsManager.Static.AsteroidObjectProviders[asteroid.AsteroidTypeName].GetAsteroidObjectShape(asteroid);
                    if (Vector3D.Distance(shape.GetClosestPoint(position), position) <= moonDiameter)
                    {
                        return true;
                    }
                }
                else
                {
                    if (child.GetType() == typeof(MySystemPlanet))
                        if (Vector3D.Distance(child.CenterPosition, position) < moonDiameter + (child as MySystemPlanet).Diameter) return true;
                }
            }

            foreach(var moon in moons)
            {
                if (Vector3D.Distance(moon.CenterPosition, position) < moonDiameter + moon.Diameter) return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to find a planet defintion for a moon, that orbits a 
        /// planet with diameter parentPlanetDiameter.
        /// </summary>
        /// <param name="parentPlanetDiameter">Diameter of the parent planet</param>
        /// <returns>The definition found for the moon</returns>
        private MyPlanetGeneratorDefinition FindMoonDefinitinon(double parentPlanetDiameter)
        {
            if (m_moons.Count <= 0) return null;

            var settings = MySettingsSession.Static.Settings.GeneratorSettings;

            int tries = 0;
            double diameter;

            MyPlanetGeneratorDefinition def;

            do
            {
                def = m_moons[MyRandom.Instance.Next(0, m_moons.Count)];
                diameter = CalculatePlanetDiameter(def);

            } while (diameter >= parentPlanetDiameter * 0.75f && ++tries <= MAX_DEF_FIND_ROUNDS);

            if(settings.SystemGenerator == SystemGenerationMethod.UNIQUE)
            {
                m_moons.Remove(def);
            }

            return def;
        }

        /// <summary>
        /// Finds a fit planet definition for a planet with a diameter less than maxDiameter.
        /// If none can be found, a random one will be returned.
        /// </summary>
        /// <param name="maxDiameter">Max diameter of the planet in meters</param>
        /// <returns>A definition of a planet that tries to be smaller than maxDiameter</returns>
        private MyPlanetGeneratorDefinition FindPlanetDefinitionForSize(double maxDiameter)
        {
            var settings = MySettingsSession.Static.Settings.GeneratorSettings;
            var planets = m_planets;
            MyPlanetGeneratorDefinition def;
            double diameter = 0;
            int tries = 0;

            if(m_planets.Count <= 0)
            {
                MyPluginLog.Debug("No planet definitions found.");
                LoadPlanetDefinitions();
                if (m_planets.Count <= 0) return null;
            }

            if(settings.SystemGenerator >= SystemGenerationMethod.MANDATORY_FIRST && m_mandatoryPlanets.Count > 0)
            {
                planets = m_mandatoryPlanets;
            }

            do
            {
                def = planets[MyRandom.Instance.Next(0, planets.Count)];
                diameter = CalculatePlanetDiameter(def);

            } while (diameter > maxDiameter && ++tries < MAX_DEF_FIND_ROUNDS);

            if(settings.SystemGenerator == SystemGenerationMethod.MANDATORY_FIRST)
            {
                m_mandatoryPlanets.Remove(def);
            }

            if(settings.SystemGenerator == SystemGenerationMethod.UNIQUE)
            {
                m_planets.Remove(def);
            }
            return def;
        }

        /// <summary>
        /// Calculates the size of a planet by its surface gravity using the world settings.
        /// </summary>
        /// <param name="planet">Planet to calculate size for</param>
        /// <returns>Diameter of the planet</returns>
        private double CalculatePlanetDiameter(MyPlanetGeneratorDefinition planet)
        {
            var settings = MySettingsSession.Static.Settings.GeneratorSettings.PlanetSettings;
            float modifier = m_gasGiants.Contains(planet) ? 2 * settings.PlanetSizeMultiplier: settings.PlanetSizeMultiplier;

            return Math.Min(Math.Sqrt(planet.SurfaceGravity * 120000 * 120000 * modifier * modifier), settings.PlanetSizeCap);
        }

        /// <summary>
        /// Filters all loaded planetary definitions loaded with this world.
        /// </summary>
        private void LoadPlanetDefinitions()
        {
            var planets = MyDefinitionManager.Static.GetPlanetsGeneratorsDefinitions();
            var settings = MySettings.Static.Settings;
            var sessionSettings = MySettingsSession.Static.Settings.GeneratorSettings;

            m_planets.Clear();
            m_suns.Clear();
            m_gasGiants.Clear();
            m_moons.Clear();
            m_mandatoryPlanets.Clear();

            foreach (var planet in planets)
            {
                string subtypeId = planet.Id.SubtypeId.String;

                if (settings.BlacklistedPlanetDefinitions.Contains(subtypeId)) continue;

                if (!sessionSettings.AllowVanillaPlanets && VANILLA_PLANETS.Contains(subtypeId)) continue;

                if (settings.MoonDefinitions.Contains(subtypeId))
                {
                    m_moons.Add(planet);
                    continue;
                }
                else if (settings.SunDefinitions.Contains(subtypeId))
                {
                    m_suns.Add(planet);
                    continue;
                }

                if (settings.MandatoryPlanetDefinitions.Contains(subtypeId))
                {
                    m_mandatoryPlanets.Add(planet);
                }

                if (settings.GasGiantDefinitions.Contains(subtypeId))
                {
                    m_gasGiants.Add(planet);
                }

                m_planets.Add(planet);
            }
        }

        /// <summary>
        /// Loads the system data xml file from the world folder, or,
        /// if none exist, returns a new one.
        /// </summary>
        /// <returns></returns>
        private MyObjectBuilder_SystemData LoadSystemData()
        {
            if (MyFileUtils.FileExistsInWorldStorage(STORAGE_FILE, typeof(MyStarSystemGenerator)))
            {
                var data = MyFileUtils.ReadXmlFileFromWorld<MyObjectBuilder_SystemData>(STORAGE_FILE, typeof(MyStarSystemGenerator));

                if(data == null)
                {
                    data = new MyObjectBuilder_SystemData();
                }

                return data;
            }
            return new MyObjectBuilder_SystemData();
        }
    }
}

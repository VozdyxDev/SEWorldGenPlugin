﻿using ProtoBuf;
using SEWorldGenPlugin.Generator.AsteroidObjects.AsteroidRing;
using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using VRage;
using VRage.Serialization;
using VRageMath;

namespace SEWorldGenPlugin.ObjectBuilders
{
    /// <summary>
    /// Serializable ObjectBuilder used to save the solar systems data and contains all system objects.
    /// </summary>
    [ProtoContract]
    [Serializable]
    public class MyObjectBuilder_SystemData
    {
        /// <summary>
        /// Objects that are located in this star system.
        /// </summary>
        [ProtoMember(1)]
        public MySystemObject CenterObject;

        /// <summary>
        /// Gets all objects currently in the system
        /// </summary>
        /// <returns>All objects</returns>
        public HashSet<MySystemObject> GetAllObjects()
        {
            HashSet<MySystemObject> objs = new HashSet<MySystemObject>();

            objs.Add(CenterObject);

            foreach (var o in CenterObject.GetAllChildren())
            {
                objs.Add(o);
            }

            return objs;
        }

        /// <summary>
        /// Returns the amount of objects in the system
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            if (CenterObject == null) return 0;
            return 1 + CenterObject.ChildCount();
        }

        /// <summary>
        /// Searches the system for an object with the given
        /// id and returns it if found, else null
        /// </summary>
        /// <param name="id">Id of the object</param>
        /// <returns>The object or null if not found</returns>
        public MySystemObject GetObjectById(Guid id)
        {
            if (id == Guid.Empty) return null;
            foreach(var child in GetAllObjects())
            {
                if (child.Id == id) return child;
            }
            return null;
        }

        /// <summary>
        /// Checks if an object with given id exists
        /// </summary>
        /// <param name="id">Id of the object</param>
        /// <returns>true if it exists</returns>
        public bool ObjectExists(Guid id)
        {
            return GetObjectById(id) != null;
        }

        /// <summary>
        /// Removes the object with the given id. Cant be the center object
        /// </summary>
        /// <param name="id">Id of the object</param>
        /// <returns>True, if object was successfully removed.</returns>
        public bool RemoveObject(Guid id)
        {
            if (id == CenterObject.Id) return false;
            if (!ObjectExists(id)) return false;

            var obj = GetObjectById(id);
            var parent = GetObjectById(obj.ParentId);

            if (parent == null) return false;

            foreach(var child in obj.GetAllChildren())
            {
                parent.ChildObjects.Add(child);
                obj.ChildObjects.Remove(child);
            }
            parent.ChildObjects.Remove(obj);

            return true;
        }
    }

    /// <summary>
    /// Baseclass for all system objects containing a type and display name
    /// </summary>
    [ProtoContract]
    [ProtoInclude(5001, typeof(MySystemPlanet))]
    [ProtoInclude(5003, typeof(MySystemAsteroids))]
    [XmlInclude(typeof(MySystemPlanet))]
    [XmlInclude(typeof(MySystemAsteroids))]
    [Serializable]
    public class MySystemObject
    {
        /// <summary>
        /// Type of the Object. Can be Moon, Planet, Ring or Belt
        /// </summary>
        [ProtoMember(1)]
        public MySystemObjectType Type;

        /// <summary>
        /// Display Name of the object, used for menus or gps
        /// </summary>
        [ProtoMember(2)]
        public string DisplayName;

        /// <summary>
        /// Id of the system object for unique identification.
        /// Is generated automatically on creation of the object
        /// </summary>
        [ProtoMember(3)]
        public Guid Id;

        /// <summary>
        /// The position of the objects center
        /// </summary>
        [ProtoMember(4)]
        public SerializableVector3D CenterPosition;

        /// <summary>
        /// The name of this objects parent
        /// </summary>
        [ProtoMember(5)]
        public Guid ParentId;

        /// <summary>
        /// All Child objects, such as moons.
        /// </summary>
        [ProtoMember(6)]
        [Serialize(MyObjectFlags.Nullable)]
        public HashSet<MySystemObject> ChildObjects;

        /// <summary>
        /// Initializes a new empty system object
        /// </summary>
        public MySystemObject()
        {
            Type = MySystemObjectType.EMPTY;
            DisplayName = "";
            CenterPosition = Vector3D.Zero;
            ChildObjects = new HashSet<MySystemObject>();
            Id = Guid.NewGuid();
            ParentId = Guid.Empty;
        }

        /// <summary>
        /// Returns the amount of objects in this tree
        /// </summary>
        /// <returns></returns>
        public int ChildCount()
        {
            int count = 0;

            if (ChildObjects == null) return 0;

            foreach(var child in ChildObjects)
            {
                count += child.ChildCount();
            }

            count += ChildObjects.Count;

            return count;
        }

        /// <summary>
        /// Gets all children of this object recursively
        /// </summary>
        /// <returns>All children</returns>
        public HashSet<MySystemObject> GetAllChildren()
        {
            HashSet<MySystemObject> children = new HashSet<MySystemObject>();

            foreach(var child in ChildObjects)
            {
                children.Add(child);
                foreach (var c in child.GetAllChildren())
                {
                    children.Add(c);
                }
            }

            return children;
        }
    }

    /// <summary>
    /// Class representing a planets data in the solar system
    /// </summary>
    [ProtoContract]
    [ProtoInclude(5004, typeof(MySystemPlanetMoon))]
    [XmlInclude(typeof(MySystemPlanetMoon))]
    [Serializable]
    public class MySystemPlanet : MySystemObject
    {
        /// <summary>
        /// The subtype id of the planet. This is the id defined for the planet in its definition file.
        /// </summary>
        [ProtoMember(4)]
        public string SubtypeId;

        /// <summary>
        /// The diameter of the planet in meters.
        /// </summary>
        [ProtoMember(5)]
        public double Diameter;

        /// <summary>
        /// If the planet is already generated, or still needs to be spawned.
        /// </summary>
        [ProtoMember(6)]
        public bool Generated;

        /// <summary>
        /// Entity id of the generated planet
        /// </summary>
        [ProtoMember(7)]
        public long EntityId;

        public MySystemPlanet()
        {
            Type = MySystemObjectType.PLANET;
            DisplayName = "";
            CenterPosition = Vector3D.Zero;
            SubtypeId = "";
            Diameter = 0;
            Generated = false;
            EntityId = 0;
        }

        /// <summary>
        /// Tries to get a ring that is a child of this planet
        /// </summary>
        /// <param name="ring">Out the ring of the planet</param>
        /// <returns>True, if a ring was found</returns>
        public bool TryGetPlanetRing(out MySystemAsteroids ring)
        {
            foreach(var child in ChildObjects)
            {
                if(child is MySystemAsteroids)
                {
                    var asteroid = child as MySystemAsteroids;
                    if (asteroid.AsteroidTypeName != MyAsteroidRingProvider.TYPE_NAME) continue;

                    ring = child as MySystemAsteroids;

                    return true;
                }
            }

            ring = null;
            return false;
        }
    }

    /// <summary>
    /// Class representing a moon of a planet in the solar system. It will always be generated,
    /// if the planet it orbits around is generated.
    /// </summary>
    [ProtoContract]
    [Serializable]
    public class MySystemPlanetMoon : MySystemPlanet
    {
        public MySystemPlanetMoon()
        {
            Type = MySystemObjectType.MOON;
            DisplayName = "";
            CenterPosition = Vector3D.Zero;
            SubtypeId = "";
            Diameter = 0;
            Generated = false;
        }
    }

    /// <summary>
    /// Class for all asteroid objects.
    /// </summary>
    [ProtoContract]
    [Serializable]
    public class MySystemAsteroids : MySystemObject
    {
        /// <summary>
        /// Name of the type this asteroid consists of.
        /// </summary>
        [ProtoMember(4)]
        public string AsteroidTypeName;

        /// <summary>
        /// The minimum and maximum size of the asteroids in this object in meters.
        /// </summary>
        [ProtoMember(5)]
        public MySerializableMinMax AsteroidSize;

        public MySystemAsteroids()
        {
            Type = MySystemObjectType.ASTEROIDS;
            DisplayName = "";
            CenterPosition = Vector3D.Zero;
            AsteroidSize = new MySerializableMinMax(0, 0);
        }
    }

    /// <summary>
    /// Enum for the body type of an object in the solar system.
    /// </summary>
    public enum MySystemObjectType
    {
        PLANET,
        MOON,
        ASTEROIDS,
        EMPTY
    }
}

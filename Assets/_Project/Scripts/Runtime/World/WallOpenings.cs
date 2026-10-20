using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// The openings (windows) cut through this wall, each recorded by the room builder that cut it.
    /// Data only: S1's editor tool <c>WallCutter</c> builds the wall's mesh around them. The wall
    /// keeps its box collider, so an opening changes nothing for physics, the NavMesh or the grid.
    /// </summary>
    /// <remarks>
    /// A wall runs through more than one room, so each builder replaces only its own openings and
    /// the mesh is rebuilt from all of them.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WallOpenings : MonoBehaviour
    {
        /// <summary>One rectangular opening, in world metres along the wall's long axis and in height.</summary>
        [Serializable]
        public struct Opening
        {
            public string Owner;
            public float AlongMin, AlongMax, Bottom, Top;
        }

        [SerializeField] List<Opening> openings = new List<Opening>();

        public List<Opening> Openings => openings;
    }
}

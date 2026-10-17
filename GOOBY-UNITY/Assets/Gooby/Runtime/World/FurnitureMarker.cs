using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>Tap target of an interactable piece of furniture (RoomView adds it; SimpleWorld raises FurnitureTapped).</summary>
    public sealed class FurnitureMarker : MonoBehaviour
    {
        public string Uid, ItemId, Kind;
        public int Rot;
        /// <summary>World bounds of the piece as placed (for the approach spot, curtains and the TV screen).</summary>
        public Bounds Bounds;

        /// <summary>The way the piece faces (its front, where Gooby stands to use it): rot 0 looks at the camera (−Z).</summary>
        public Vector3 Front => PropPlaceholder.Rotation(Rot) * Vector3.forward;
    }
}

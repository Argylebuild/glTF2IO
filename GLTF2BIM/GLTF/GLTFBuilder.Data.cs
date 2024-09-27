using System;
using System.Collections.Generic;
using System.Linq;

using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.BufferSegments.BaseTypes;

namespace GLTF2BIM.GLTF {
    public sealed partial class GLTFBuilder {
        string _name;
        public readonly glTF _gltf = null;
        public readonly List<BufferSegment> _bufferSegments = new List<BufferSegment>();
        public readonly Queue<glTFMeshPrimitive> _primQueue = new Queue<glTFMeshPrimitive>();
        public readonly Dictionary<string, int> meshesInstancing;
        public readonly Dictionary<string, uint> materialsInstancing;
    }
}

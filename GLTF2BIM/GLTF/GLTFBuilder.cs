using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

using GLTF2BIM.Properties;
using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.BufferSegments;
using GLTF2BIM.GLTF.Package;
using GLTF2BIM.GLTF.Package.BaseTypes;
using GLTF2BIM.GLTF.BufferSegments.BaseTypes;
using System.Text;

namespace GLTF2BIM.GLTF {
    public sealed partial class GLTFBuilder {

        public GLTFBuilder(string name) {
            _name = name;
            _gltf = new glTF();
            meshesInstancing = new Dictionary<string, int>();
            materialsInstancing = new Dictionary<string, uint>();
        }

        /// <summary>
        /// Pack the constructed glTF data into a container
        /// </summary>
        public List<GLTFPackageItem> Pack(GLTFBuilderOptions options) {
            // TODO: Add glb option
            options = options ?? new GLTFBuilderOptions();

            int CreateBuffer(List<BufferSegment> segments, int startIndex, int bufferCounter, out byte[] binData) {
                // add the buffers to the gltf and to the bundle
                List<byte> bufferBytes = new List<byte>();

                for (int sidx = startIndex; sidx < segments.Count; sidx++) {
                    // get the segment bytes
                    var seg = segments[sidx];
                    var bytes = seg.ToByteArray();

                    // align the data correctly
                    uint dataSize = 0;
                    switch (seg.DataType) {
                        case glTFAccessorComponentType.SHORT:
                        case glTFAccessorComponentType.UNSIGNED_SHORT:
                            dataSize = 2;
                            break;
                        case glTFAccessorComponentType.UNSIGNED_INT:
                        case glTFAccessorComponentType.FLOAT:
                            dataSize = 4;
                            break;
                    }
                    // calculate necessary padding
                    int padding = 0;
                    if (dataSize > 0) {
                        var bufferSize = bufferBytes.Count;
                        // cast to double in important as the result of length/size
                        // might be larger than the single precision
                        padding = (int)(Math.Ceiling(bufferSize / (double)dataSize) * dataSize) - bufferSize;
                    }

                    // if buffer has enough space (for new data plus previous padding)
                    if ((bufferBytes.Count + padding + bytes.Length) < options.MaxBinarySize) {
                        // add padding
                        if (padding > 0)
                            bufferBytes.AddRange(new byte[padding]);

                        // make the buffer view now
                        var bufferView = new glTFBufferView {
                            Buffer = (uint)bufferCounter,
                            ByteLength = (uint)bytes.Length,
                            ByteOffset = (uint)bufferBytes.Count,
                            Target = seg.Target
                        };
                        _gltf.BufferViews.Add(bufferView);

                        // add the data to buffer
                        bufferBytes.AddRange(bytes);

                        var accessor = new glTFAccessor {
                            Type = seg.Type,
                            ComponentType = seg.DataType,
                            BufferView = (uint)_gltf.BufferViews.Count - 1,
                            ByteOffset = 0,
                            Count = seg.Count,
                            Min = seg.Min,
                            Max = seg.Max,
                        };
                        _gltf.Accessors.Add(accessor);
                    }
                    // otherwise return the index of this segment,
                    // that wasn't included in this buffer
                    else {
                        // if buffer is empty, this segment is too large for 
                        // max buffer size
                        if (bufferBytes.Count == 0) {
                            throw new Exception($"A single segment data is larger than max buffer size ({bytes.Length} > {options.MaxBinarySize})");
                        }
                        // grab all the collected data
                        binData = bufferBytes.ToArray();
                        // and return index of segment to start with in the next
                        // buffer
                        return sidx;
                    }
                    // move on to the next segment
                }

                // only if all the segments fit inside the binary buffer,
                // code gets to there. reu
                binData = bufferBytes.ToArray();
                return segments.Count;
            }

            // create a gltf bundle
            var bundleItems = new List<GLTFPackageItem>();

            // while new buffer can be made from the segments and not all the
            // segments were fit inside the created buffer, keep making buffers
            int bufferIndex = 0;
            bool creatingFirstBuffer = true;
            bool allSegmentsDone = true;
            int segmentStartIndex = 0;
            do {
                var lastIndex = CreateBuffer(_bufferSegments, segmentStartIndex, bufferIndex, out var binData);
                creatingFirstBuffer = (bufferIndex == 0);
                allSegmentsDone = lastIndex == _bufferSegments.Count;

                if (creatingFirstBuffer && options.UseSingleBinary && !allSegmentsDone)
                    throw new Exception("Data is too large for a single buffer. Disable the single binary export option");

                // otherwise add the binary buffer to the package and move on
                string bufferName = $"{_name}{bufferIndex}.bin";
                if (options.UseSingleBinary)
                    bufferName = $"{_name}.bin";
                bundleItems.Add(
                    new GLTFPackageBinaryItem(bufferName, binData)
                    );

                var buffer = new glTFBuffer {
                    ByteLength = (uint)binData.Count(),
                    // assuming buffer is stored alongside gltf
                    Uri = bufferName
                };
                _gltf.Buffers.Add(buffer);

                bufferIndex++;
                segmentStartIndex = lastIndex;
            }
            while (!allSegmentsDone);

            // store snapshot of collected data into a gltf structure
            var model = new GLTFPackageModelItem(
                uri: $"{_name}.gltf",
                modelData: JsonConvert.SerializeObject(
                    _gltf,
                    new JsonSerializerSettings {
                        NullValueHandling = NullValueHandling.Ignore
                    }
                )
            );

            // finally add glTF model to the bundle
            bundleItems.Add(model);

            return bundleItems;
        }

        #region Asset
        public void SetAsset(string generatorId, string copyright,
                             glTFExtension[] exts, glTFExtras extras) {
            var assetExts = new Dictionary<string, glTFExtension>();
            if (exts != null) {
                foreach (var ext in exts)
                    if (ext != null) {
                        assetExts.Add(ext.Name, ext);
                        UseExtension(ext);
                    }
            }

            _gltf.Asset = new glTFAsset {
                Generator = generatorId,
                Copyright = copyright,
                Extensions = assetExts.Count > 0 ? assetExts : null,
                Extras = extras
            };
        }

        public void UseExtension(glTFExtension ext) {
            if (_gltf.ExtensionsUsed is null)
                _gltf.ExtensionsUsed = new HashSet<string>();
            _gltf.ExtensionsUsed.Add(ext.Name);
        }

        public glTFAsset PeekAsset() => _gltf.Asset;
        #endregion

        #region Scenes
        public uint SceneCount => (uint)_gltf.Scenes.Count;

        public uint OpenScene(string name,
                              glTFExtension[] exts, glTFExtras extras) {
            _gltf.Scenes.Add(
                new glTFScene {
                    Name = name,
                    Extensions = exts?.ToDictionary(x => x.Name, x => x),
                    Extras = extras
                }
                );
            return (uint)_gltf.Scenes.Count - 1;
        }

        public glTFScene PeekScene() => _gltf.Scenes.LastOrDefault();

        public int FindScene(Func<glTFScene, bool> filter) {
            foreach (var scene in _gltf.Scenes)
                if (filter(scene))
                    return _gltf.Scenes.IndexOf(scene);
            return -1;
        }

        public void CloseScene() { }
        #endregion

        #region Nodes
        public uint NodeCount => (uint)_gltf.Nodes.Count();

        public uint AppendNode(string name, float[] matrix,
                               glTFExtension[] exts, glTFExtras extras) {

            // create new node and set base properties
            var node = new glTFNode() {
                Name = name ?? "undefined",
                Matrix = matrix,
                Extensions = exts?.ToDictionary(x => x.Name, x => x),
                Extras = extras
            };

            var idx = _gltf.Nodes.Append(node);
            return AppendNodeToScene(idx);
        }

        public uint OpenNode(string name, float[] matrix,
                             glTFExtension[] exts, glTFExtras extras) {
            var idx = AppendNode(name, matrix, exts, extras);
            _gltf.Nodes.Open(idx);
            return idx;
        }

        public glTFNode PeekNode() => _gltf.Nodes.LastOrDefault();

        public glTFNode GetNode(uint idx) => _gltf.Nodes[idx];

        public uint GetNodeIndex(glTFNode node) => _gltf.Nodes.IndexOf(node);

        public glTFNode GetActiveNode() => _gltf.Nodes.Peek();

        public int FindNode(Func<glTFNode, bool> filter) {
            foreach (var node in _gltf.Nodes)
                if (filter(node))
                    return (int)_gltf.Nodes.IndexOf(node);
            return -1;
        }

        public int FindChildNode(Func<glTFNode, bool> filter) {
            if (_gltf.Nodes.Peek() is glTFNode currentNode) {
                if (currentNode.Children is null)
                    return -1;

                foreach (var childIdx in currentNode.Children) {
                    var node = _gltf.Nodes[childIdx];
                    if (filter(node))
                        return (int)childIdx;
                }
                return -1;
            }
            else
                return FindNode(filter);
        }

        public int FindParentNode(uint nodeIndex) {
            foreach (var node in _gltf.Nodes)
                if (node.Children != null && node.Children.Contains(nodeIndex))
                    return (int)_gltf.Nodes.IndexOf(node);
            return -1;
        }

        public uint OpenExistingNode(uint nodeIndex) {
            if (_gltf.Nodes.Contains(nodeIndex)) {
                AppendNodeToScene(nodeIndex);
                _gltf.Nodes.Open(nodeIndex);
                return nodeIndex;
            }
            else
                throw new Exception(StringLib.NodeNotExist);
        }

        public void CloseNode() {
            if (PeekNode() is glTFNode currentNode) {
                if (_primQueue.Count > 0) {

                    // searching for an already existent mesh
                    int meshIdx = SearchMesh(_primQueue.ToList());

                    // if it aready exists reuse it, otherwise, create a new mesh
                    meshIdx = (meshIdx >= 0) ? meshIdx : CreateMesh();

                    // set the mesh on the active node
                    currentNode.Mesh = (uint)meshIdx;
                }

                // and close the node
                _gltf.Nodes.Close();

                // clean queue
                _primQueue.Clear();
            }
        }

        private int SearchMesh(List<glTFMeshPrimitive> primitives)
        {
            var key = GetPrimitivesKey(primitives);

            int meshIdx = default;

            if (meshesInstancing.TryGetValue(key, out meshIdx))
                return meshIdx;

            return -1;
        }
        private uint? SearchMaterial(string name, float[] color)
        {
            var key = GetMaterialKey(name, color);

            uint materialIdx = default;

            if (materialsInstancing.TryGetValue(key, out materialIdx))
                return materialIdx;

            return null; 
        }

        private int CreateMesh()
        {
            var primitives = _primQueue.ToList();

            var mesh = new glTFMesh
            {
                Primitives = primitives
            };

            // create a new mesh
            _gltf.Meshes.Add(mesh);
            var meshIdx = _gltf.Meshes.Count - 1;
            meshesInstancing.Add(GetPrimitivesKey(primitives), meshIdx);

            return meshIdx;
        }
        private uint CreateMaterial(string name, float[] color, glTFExtension[] exts, glTFExtras extras)
        {
            // it is a new material, proceed to add it!
            var material = new glTFMaterial()
            {
                Name = name,
                PBRMetallicRoughness = new glTFPBRMetallicRoughness()
                {
                    BaseColorFactor = color,
                    MetallicFactor = 0f,
                    RoughnessFactor = 1f,
                },
                Extensions = exts?.ToDictionary(x => x.Name, x => x),
                Extras = extras
            };

            _gltf.Materials.Add(material);
            var materialIdx = (uint)_gltf.Materials.Count - 1;
            materialsInstancing.Add(GetMaterialKey(name, color), materialIdx);

            return materialIdx;
        }

        public string GetPrimitivesKey(List<glTFMeshPrimitive> primitives)
        {
            StringBuilder keyBuilder = new StringBuilder();

            foreach (var primitive in primitives)
            {
                keyBuilder.Append($"{primitive.Attributes.Normal}:{primitive.Attributes.Position}:{primitive.Indices}:{primitive.Material}:{primitive.Mode}:");
            }

            return keyBuilder.ToString();
        }

        public string GetMaterialKey(string name, float[] color)
        {
            return $":{name}:{color[0]}:{color[1]}:{color[2]}";
        }


        #endregion

        #region Node Mesh
        public uint AddPrimitive(float[] vertices, float[] normals, uint[] faces) {
            // ensure vertex and face data is available
            if (vertices is null || faces is null)
                throw new Exception(StringLib.VertexFaceIsRequired);

            if (PeekNode() is glTFNode) {
                // process vertex data
                var vertexBuffer = new BufferVectorSegment(vertices);
                var vBuffIdx = _bufferSegments.IndexOf(vertexBuffer);
                if (vBuffIdx < 0) {
                    _bufferSegments.Add(vertexBuffer);
                    vBuffIdx = _bufferSegments.Count - 1;
                }

                // process normal data if available
                int nBuffIdx = -1;
                if (normals != null) {
                    var normalBuffer = new BufferVectorSegment(normals);
                    nBuffIdx = _bufferSegments.IndexOf(normalBuffer);
                    if (nBuffIdx < 0) {
                        _bufferSegments.Add(normalBuffer);
                        nBuffIdx = _bufferSegments.Count - 1;
                    }
                }

                // process face data
                uint maxIndex = faces.Max();
                BufferSegment faceBuffer;
                if (maxIndex <= 0xFF) {
                    var byteFaces = new List<byte>();
                    foreach (var face in faces)
                        byteFaces.Add(Convert.ToByte(face));
                    faceBuffer = new BufferScalar1Segment(byteFaces.ToArray());
                }
                else if (maxIndex <= 0xFFFF) {
                    var shortFaces = new List<ushort>();
                    foreach (var face in faces)
                        shortFaces.Add(Convert.ToUInt16(face));
                    faceBuffer = new BufferScalar2Segment(shortFaces.ToArray());
                }
                else {
                    faceBuffer = new BufferScalar4Segment(faces);
                }

                var fBuffIdx = _bufferSegments.IndexOf(faceBuffer);
                if (fBuffIdx < 0) {
                    _bufferSegments.Add(faceBuffer);
                    fBuffIdx = _bufferSegments.Count - 1;
                }

                // queue the primitive
                _primQueue.Enqueue(
                    new glTFMeshPrimitive {
                        Indices = (uint)fBuffIdx,
                        Attributes = new glTFAttributes {
                            Position = (uint)vBuffIdx,
                            Normal = nBuffIdx >= 0 ? (uint)nBuffIdx : (uint?)null
                        }
                    }
                );

                // return primitive index
                return (uint)_primQueue.Count - 1;
            }
            else
                throw new Exception(StringLib.NoParentNode);
        }

        public uint AddMaterial(uint primitiveIndex,
                                string name, float[] color,
                                glTFExtension[] exts, glTFExtras extras) {
            if (PeekNode() is glTFNode currentNode) {
                if (_primQueue.Count > primitiveIndex) {
                    var prim = _primQueue.ElementAt((int)primitiveIndex);

                    if (_gltf.Materials is null)
                        _gltf.Materials = new List<glTFMaterial>();

                    // searching for an already existent material
                    var materialIdx = SearchMaterial(name, color);

                    // if it aready exists reuse it, otherwise, create a new material
                    prim.Material = (materialIdx == null) ? CreateMaterial(name, color, exts, extras) : materialIdx;

                    return prim.Material.Value;
                }
                else
                    throw new Exception(StringLib.NoParentPrimitive);
            }
            else
                throw new Exception(StringLib.NoParentNode);
        }


        public int FindMaterial(Func<glTFMaterial, bool> filter) {
            if (_gltf.Materials != null && _gltf.Materials.Count > 0) {
                foreach (var material in _gltf.Materials)
                    if (filter(material))
                        return (int)_gltf.Materials.IndexOf(material);
            }
            return -1;
        }

        public void UpdateMaterial(uint primitiveIndex, uint materialIndex) {
            if (PeekNode() is glTFNode) {
                if (_primQueue.Count > primitiveIndex) {
                    var prim = _primQueue.ElementAt((int)primitiveIndex);
                    prim.Material = materialIndex;
                }
                else
                    throw new Exception(StringLib.NoParentPrimitive);
            }
            else
                throw new Exception(StringLib.NoParentNode);
        }
        #endregion
    }
}

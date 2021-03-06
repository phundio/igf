﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BEPUphysicsDrawer.Models
{
    /// <summary>
    /// Manages batching of display models and their drawing.
    /// </summary>
    public class ModelDisplayObjectBatch
    {
        /// <summary>
        /// Maximum number of display objects that can be lumped into a single display batch.
        /// </summary>
        public const int MaximumObjectsPerBatch = 81;

        /// <summary>
        /// Maximum number of primitives that can be batched together in a single draw call.
        /// </summary>
        public static int MaximumPrimitiveCountPerBatch = 65535;

        private readonly GraphicsDevice graphicsDevice;
        private readonly List<ushort> indexList = new List<ushort>();
        private readonly List<ModelDisplayObject> displayObjects = new List<ModelDisplayObject>();
        private readonly ReadOnlyCollection<ModelDisplayObject> myDisplayObjectsReadOnly;

        /// <summary>
        /// List of textures associated with display objects in the batch.
        /// </summary>
        private readonly int[] textureIndices = new int[MaximumObjectsPerBatch];

        //These lists are used to collect temporary data from display objects.
        private readonly List<VertexPositionNormalTexture> vertexList = new List<VertexPositionNormalTexture>();

        /// <summary>
        /// List of all world transforms associated with display objects in the batch.
        /// </summary>
        private readonly Matrix[] worldTransforms = new Matrix[MaximumObjectsPerBatch];

        private IndexBuffer indexBuffer;
        private ushort[] indices = new ushort[0];

        /// <summary>
        /// Contains instancing data to be fed into the second stream.
        /// </summary>
        private VertexBuffer instancingBuffer;

        private InstancedVertex[] instancedVertices = new InstancedVertex[0];
        private VertexBuffer vertexBuffer;
        private VertexPositionNormalTexture[] vertices = new VertexPositionNormalTexture[0];
        private VertexBufferBinding[] bindings;

        //Has VertexBuffer, IndexBuffer, second stream for indices.
        //Why second stream?  Creating it will be easier since can addRange on lists gotten from ModelDisplayObject, then toss in
        //the appropriate indices separately.


        /// <summary>
        /// Constructs a new batch.
        /// </summary>
        /// <param name="graphicsDevice">Device to use when drawing.</param>
        public ModelDisplayObjectBatch(GraphicsDevice graphicsDevice)
        {
            this.graphicsDevice = graphicsDevice;
            myDisplayObjectsReadOnly = new ReadOnlyCollection<ModelDisplayObject>(displayObjects);
        }

        /// <summary>
        /// Gets the display objects in this batch.
        /// </summary>
        public ReadOnlyCollection<ModelDisplayObject> DisplayObjects
        {
            get { return myDisplayObjectsReadOnly; }
        }

        /// <summary>
        /// Adds a display object to the batch.
        /// </summary>
        /// <param name="displayObject">Display object to add.</param>
        /// <param name="drawer">Drawer of the batch.</param>
        public bool Add(ModelDisplayObject displayObject, InstancedModelDrawer drawer)
        {
            //In theory, don't need to test for dupliate entries since batch.Add
            //should only be called through a InstancedModelDrawer's add (which checks beforehand).
            if (displayObjects.Count == MaximumObjectsPerBatch ||
                ((indices.Length / 3 + displayObject.GetTriangleCountEstimate()) > MaximumPrimitiveCountPerBatch &&
                 displayObjects.Count > 0))
                return false;
            displayObjects.Add(displayObject);
            int instanceIndex = displayObjects.Count - 1;
            var textureIndex = displayObject.TextureIndex;
            displayObject.GetVertexData(vertexList, indexList, this, (ushort)vertices.Length, indices.Length, instanceIndex);
            //Add the data to the batch.
            var newVertices = new VertexPositionNormalTexture[vertices.Length + vertexList.Count];
            vertices.CopyTo(newVertices, 0);
            vertexList.CopyTo(newVertices, vertices.Length);
            vertices = newVertices;

            var newIndices = new ushort[indices.Length + indexList.Count];
            indices.CopyTo(newIndices, 0);
            indexList.CopyTo(newIndices, indices.Length);
            indices = newIndices;

            var newInstancingVertices = new InstancedVertex[instancedVertices.Length + vertexList.Count];
            instancedVertices.CopyTo(newInstancingVertices, 0);
            for (int i = instancedVertices.Length; i < newInstancingVertices.Length; i++)
                newInstancingVertices[i] = new InstancedVertex { InstanceIndex = instanceIndex, TextureIndex = textureIndex };
            instancedVertices = newInstancingVertices;

            vertexBuffer = new VertexBuffer(graphicsDevice, VertexPositionNormalTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            vertexBuffer.SetData(vertices);
            instancingBuffer = new VertexBuffer(graphicsDevice, InstancedVertex.VertexDeclaration, instancedVertices.Length, BufferUsage.WriteOnly);
            instancingBuffer.SetData(instancedVertices);
            bindings = new VertexBufferBinding[] { vertexBuffer, instancingBuffer };
            indexBuffer = new IndexBuffer(graphicsDevice, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
            indexBuffer.SetData(indices);

            vertexList.Clear();
            indexList.Clear();
            return true;
        }

        /// <summary>
        /// Removes a display object from the batch.
        /// </summary>
        /// <param name="displayObject">Display object to remove.</param>
        /// <param name="drawer">Instanced model drawer doing the removal.</param>
        public void Remove(ModelDisplayObject displayObject, InstancedModelDrawer drawer)
        {
            //Modify vertex buffer
            var newVertices = new VertexPositionNormalTexture[vertices.Length - displayObject.BatchInformation.VertexCount];
            //Copy the first part back (before the display object)
            Array.Copy(vertices, 0, newVertices, 0, displayObject.BatchInformation.BaseVertexBufferIndex);
            //Copy the second part back (after the display object)
            Array.Copy(vertices, displayObject.BatchInformation.BaseVertexBufferIndex + displayObject.BatchInformation.VertexCount,
                       newVertices, displayObject.BatchInformation.BaseVertexBufferIndex,
                       vertices.Length - (displayObject.BatchInformation.BaseVertexBufferIndex + displayObject.BatchInformation.VertexCount));
            vertices = newVertices;



            //Modify index buffer
            var newIndices = new ushort[indices.Length - displayObject.BatchInformation.IndexCount];
            //Copy the first part back (before the display object)
            Array.Copy(indices, 0, newIndices, 0, displayObject.BatchInformation.BaseIndexBufferIndex);
            //Copy the second part back (after the display object)
            Array.Copy(indices, displayObject.BatchInformation.BaseIndexBufferIndex + displayObject.BatchInformation.IndexCount,
                       newIndices, displayObject.BatchInformation.BaseIndexBufferIndex,
                       indices.Length - (displayObject.BatchInformation.BaseIndexBufferIndex + displayObject.BatchInformation.IndexCount));
            indices = newIndices;
            //The index buffer's data is now wrong though.  We deleted a bunch of vertices.
            //So go through the index buffer starting at the point of deletion and decrease the values appropriately.
            for (int i = displayObject.BatchInformation.BaseIndexBufferIndex; i < indices.Length; i++)
                indices[i] -= (ushort)displayObject.BatchInformation.VertexCount;

            //Modify instancing indices
            var newInstancedVertices = new InstancedVertex[instancedVertices.Length - displayObject.BatchInformation.VertexCount];
            //Copy the first part back (before the display object)
            Array.Copy(instancedVertices, 0, newInstancedVertices, 0, displayObject.BatchInformation.BaseVertexBufferIndex);
            //Copy the second part back (after the display object)
            Array.Copy(instancedVertices, displayObject.BatchInformation.BaseVertexBufferIndex + displayObject.BatchInformation.VertexCount,
                       newInstancedVertices, displayObject.BatchInformation.BaseVertexBufferIndex,
                       instancedVertices.Length - (displayObject.BatchInformation.BaseVertexBufferIndex + displayObject.BatchInformation.VertexCount));
            instancedVertices = newInstancedVertices;
            //Like with the index buffer, go through the buffer starting at the point of deletion and decrease the values appropriately.
            for (int i = displayObject.BatchInformation.BaseVertexBufferIndex; i < instancedVertices.Length; i++)
                instancedVertices[i].InstanceIndex--;

            displayObjects.Remove(displayObject);
            //Move the subsequent display objects list indices and base vertices/indices.
            for (int i = displayObject.BatchInformation.BatchListIndex; i < DisplayObjects.Count; i++)
            {
                DisplayObjects[i].BatchInformation.BatchListIndex--;
                DisplayObjects[i].BatchInformation.BaseVertexBufferIndex -= displayObject.BatchInformation.VertexCount;
                DisplayObjects[i].BatchInformation.BaseIndexBufferIndex -= displayObject.BatchInformation.IndexCount;
            }
            // Tell ModelDisplayObject that it got removed (batch, indices).
            displayObject.ClearBatchReferences();


            //If there weren't any vertices, this batch is about to die.
            if (displayObjects.Count > 0)
            {
                vertexBuffer = new VertexBuffer(graphicsDevice, VertexPositionNormalTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
                vertexBuffer.SetData(vertices);

                instancingBuffer = new VertexBuffer(graphicsDevice, InstancedVertex.VertexDeclaration, instancedVertices.Length, BufferUsage.WriteOnly);
                instancingBuffer.SetData(instancedVertices);

                indexBuffer = new IndexBuffer(graphicsDevice, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
                indexBuffer.SetData(indices);

                bindings = new VertexBufferBinding[] { vertexBuffer, instancingBuffer };
            }
        }

        /// <summary>
        /// Updates the display objects within the batch and collects transforms.
        /// </summary>
        public void Update()
        {
            for (int i = 0; i < displayObjects.Count; i++)
            {
                displayObjects[i].Update();
                worldTransforms[i] = displayObjects[i].WorldTransform;
                textureIndices[i] = displayObjects[i].TextureIndex;
            }
        }

        /// <summary>
        /// Draws the models managed by the batch.
        /// </summary>
        public void Draw(Effect effect, EffectParameter worldTransformsParameter, EffectParameter textureIndicesParameter, EffectPass pass)
        {
            if (vertices.Length > 0)
            {
                graphicsDevice.SetVertexBuffers(bindings);
                graphicsDevice.Indices = indexBuffer;
                worldTransformsParameter.SetValue(worldTransforms);
                textureIndicesParameter.SetValue(textureIndices);
                pass.Apply();

                graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                                                     0, 0, vertices.Length,
                                                     0, indices.Length / 3);
            }
        }

        internal struct InstancedVertex
        {
            /// <summary>
            /// Index of the instance to which this vertex belongs.
            /// </summary>
            public float InstanceIndex;
            /// <summary>
            /// Index of the texture used by the vertex.
            /// </summary>
            public float TextureIndex;

            public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(new[] 
            { 
                new VertexElement(0, VertexElementFormat.Single, VertexElementUsage.TextureCoordinate, 1),
                new VertexElement(4, VertexElementFormat.Single, VertexElementUsage.TextureCoordinate, 2)
            });
        }
    }
}
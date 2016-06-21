﻿using System;
using System.Collections.Generic;
using System.Numerics;
using Veldrid.Graphics;

namespace Ge.Graphics
{
    public class MeshRenderer : Component, RenderItem
    {
        private static readonly string[] s_stages = { "Standard" };

        private readonly DynamicDataProvider<Matrix4x4> _worldProvider;
        private readonly DependantDataProvider<Matrix4x4> _inverseTransposeWorldProvider;
        private readonly DynamicDataProvider<TintInfo> _tintInfoProvider;
        private readonly ConstantBufferDataProvider[] _perObjectProviders;
        private readonly VertexPositionNormalTexture[] _vertices;
        private readonly int[] _indices;
        private readonly TextureData _texture;

        private VertexBuffer _vb;
        private IndexBuffer _ib;
        private Material _material;

        private static RasterizerState s_wireframeRS;

        public bool Wireframe { get; set; } = false;

        public TintInfo Tint { get { return _tintInfoProvider.Data; } set { _tintInfoProvider.Data = value; } }

        public MeshRenderer(VertexPositionNormalTexture[] vertices, int[] indices, TextureData texture)
        {
            _worldProvider = new DynamicDataProvider<Matrix4x4>();
            _inverseTransposeWorldProvider = new DependantDataProvider<Matrix4x4>(_worldProvider, CalculateInverseTranspose);
            _tintInfoProvider = new DynamicDataProvider<TintInfo>();
            _perObjectProviders = new ConstantBufferDataProvider[] { _worldProvider, _inverseTransposeWorldProvider, _tintInfoProvider };
            _vertices = vertices;
            _indices = indices;
            _texture = texture;
        }

        public RenderOrderKey GetRenderOrderKey()
        {
            return new RenderOrderKey();
        }

        public IEnumerable<string> GetStagesParticipated()
        {
            return s_stages;
        }

        public void Render(RenderContext rc, string pipelineStage)
        {
            _worldProvider.Data = GameObject.Transform.GetWorldMatrix();

            rc.SetVertexBuffer(_vb);
            rc.SetIndexBuffer(_ib);
            rc.SetMaterial(_material);
            if (Wireframe)
            {
                rc.SetRasterizerState(s_wireframeRS);
            }
            else
            {
                rc.SetRasterizerState(rc.DefaultRasterizerState);
            }

            _material.ApplyPerObjectInputs(_perObjectProviders);

            rc.DrawIndexedPrimitives(_indices.Length, 0);
        }

        public override void Attached(SystemRegistry registry)
        {
            var gs = registry.GetSystem<GraphicsSystem>();
            InitializeContextObjects(gs.Context);
            gs.AddRenderItem(this);
        }

        public override void Removed(SystemRegistry registry)
        {
            registry.GetSystem<GraphicsSystem>().RemoveRenderItem(this);
            ClearDeviceResources();
        }

        private void InitializeContextObjects(RenderContext context)
        {
            ResourceFactory factory = context.ResourceFactory;

            _vb = factory.CreateVertexBuffer(VertexPositionNormalTexture.SizeInBytes * _vertices.Length, false);
            VertexDescriptor desc = new VertexDescriptor(VertexPositionNormalTexture.SizeInBytes, VertexPositionNormalTexture.ElementCount, 0, IntPtr.Zero);
            _vb.SetVertexData(_vertices, desc);

            _ib = factory.CreateIndexBuffer(sizeof(int) * _indices.Length, false);
            _ib.SetIndices(_indices);

            MaterialVertexInput materialInputs = new MaterialVertexInput(
                VertexPositionNormalTexture.SizeInBytes,
                new MaterialVertexInputElement[]
                {
                    new MaterialVertexInputElement("in_position", VertexSemanticType.Position, VertexElementFormat.Float3),
                    new MaterialVertexInputElement("in_normal", VertexSemanticType.Normal, VertexElementFormat.Float3),
                    new MaterialVertexInputElement("in_texCoord", VertexSemanticType.TextureCoordinate, VertexElementFormat.Float2)
                });

            MaterialInputs<MaterialGlobalInputElement> globalInputs = new MaterialInputs<MaterialGlobalInputElement>(
                new MaterialGlobalInputElement[]
                {
                    new MaterialGlobalInputElement("projectionMatrixUniform", MaterialInputType.Matrix4x4, context.DataProviders["ProjectionMatrix"]),
                    new MaterialGlobalInputElement("viewMatrixUniform", MaterialInputType.Matrix4x4, context.DataProviders["ViewMatrix"]),
                    new MaterialGlobalInputElement("LightBuffer", MaterialInputType.Custom, context.DataProviders["LightBuffer"]),
                });

            MaterialInputs<MaterialPerObjectInputElement> perObjectInputs = new MaterialInputs<MaterialPerObjectInputElement>(
                new MaterialPerObjectInputElement[]
                {
                    new MaterialPerObjectInputElement("WorldMatrix", MaterialInputType.Matrix4x4, _worldProvider.DataSizeInBytes),
                    new MaterialPerObjectInputElement("InverseTransposeWorldMatrixUniform", MaterialInputType.Matrix4x4, _inverseTransposeWorldProvider.DataSizeInBytes),
                    new MaterialPerObjectInputElement("TintInfoBuffer", MaterialInputType.Float4, _tintInfoProvider.DataSizeInBytes),
                });

            MaterialTextureInputs textureInputs = new MaterialTextureInputs(
                new MaterialTextureInputElement[]
                {
                    new TextureDataInputElement("surfaceTexture", _texture)
                });

            _material = factory.CreateMaterial(
                context,
                VertexShaderSource,
                FragmentShaderSource,
                materialInputs,
                globalInputs,
                perObjectInputs,
                textureInputs);

            if (s_wireframeRS == null)
            {
                s_wireframeRS = factory.CreateRasterizerState(FaceCullingMode.None, TriangleFillMode.Wireframe, true, true);
            }
        }

        private Matrix4x4 CalculateInverseTranspose(Matrix4x4 m)
        {
            Matrix4x4 inverted;
            Matrix4x4.Invert(m, out inverted);
            return Matrix4x4.Transpose(inverted);
        }

        public void ClearDeviceResources()
        {
            _vb.Dispose();
            _ib.Dispose();
            _material.Dispose();
        }

        private static readonly string VertexShaderSource = "textured-vertex";
        private static readonly string FragmentShaderSource = "lit-frag";
    }

    public struct TintInfo
    {
        public readonly Vector3 Color;
        public readonly float TintFactor;

        public TintInfo(Vector3 color, float tintFactor)
        {
            Color = color;
            TintFactor = tintFactor;
        }
    }
}

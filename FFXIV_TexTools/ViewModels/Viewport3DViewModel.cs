﻿// FFXIV TexTools
// Copyright © 2019 Rafael Gonzalez - All Rights Reserved
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using FFXIV_TexTools.Custom;
using FFXIV_TexTools.Properties;
using FFXIV_TexTools.Resources;
using FFXIV_TexTools.Views;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.Wpf.SharpDX.Cameras;
using Newtonsoft.Json.Linq;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.Direct3D9;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Models.Helpers;
using MeshBuilder = HelixToolkit.Wpf.SharpDX.MeshBuilder;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;
using PerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
using TextureModel = HelixToolkit.Wpf.SharpDX.TextureModel;
using Vector4 = System.Numerics.Vector4;

namespace FFXIV_TexTools.ViewModels
{
    public class Viewport3DViewModel : BaseViewPortViewModel, INotifyPropertyChanged
    {
        private string _backgroundColor;
        private Vector3D _light1Direction;
        private Vector3D _light2Direction;
        private Vector3D _light3Direction;
        protected double _lightX, _light1X, _light2X, _lightY, _light1Y, _light2Y, _lightZ, _light1Z, _light2Z;
        private bool _renderLight3;
        private static readonly Regex bodyMaterial = new Regex("[bf][0-9]{4}", RegexOptions.IgnoreCase);

        public delegate void ViewportEventHandler(Viewport3DViewModel owner);
        public event ViewportEventHandler TextureUpdateRequested;
        public event ViewportEventHandler ZoomExtentsRequested;

        public ObservableElement3DCollection Models { get; } = new ObservableElement3DCollection();

        public Viewport3DViewModel()
        {
            Title = "";
            SubTitle = "";

            // Eat exception to not immediately crash in VirtualBox
            try
            {
                EffectsManager = new CustomEffectsManager();
            } catch { }

            Camera = new PerspectiveCamera();

            BackgroundColor = Properties.Settings.Default.BG_Color;
            ReflectionLabel = $"{UIStrings.Reflection}  |  {ReflectionValue}";
        }

        private MeshGeometry3D GetMeshGeometry(TTModel model, int meshGroupId)
        {
            var group = model.MeshGroups[meshGroupId];
            var mg = new MeshGeometry3D
            {
                Positions = new Vector3Collection((int)group.VertexCount),
                Normals = new Vector3Collection((int)group.VertexCount),
                Colors = new Color4Collection((int)group.VertexCount),
                TextureCoordinates = new Vector2Collection((int)group.VertexCount),
                BiTangents = new Vector3Collection((int)group.VertexCount),
                Tangents = new Vector3Collection((int)group.VertexCount),
                Indices = new IntCollection((int)group.IndexCount)
            };

            var indexCount = 0;
            var vertCount = 0;

            for (int mi = 0; mi < meshGroupId; mi++)
            {
                var g = model.MeshGroups[mi];
                //vertCount += (int) g.VertexCount;
                //indexCount += (int)g.IndexCount;
            }

            foreach (var p in group.Parts)
            {
                foreach (var v in p.Vertices) {

                    // I don't think our current shader actually utilizes this data anyways
                    // but may as well include it correctly.
                    var color = new Color4();
                    color.Red = v.VertexColor[0] / 255f;
                    color.Green = v.VertexColor[1] / 255f;
                    color.Blue = v.VertexColor[2] / 255f;
                    color.Alpha = v.VertexColor[3] / 255f;

                    mg.Positions.Add(v.Position);
                    mg.Normals.Add(v.Normal);
                    mg.TextureCoordinates.Add(v.UV1);
                    mg.Colors.Add(color);
                    mg.BiTangents.Add(v.Binormal);
                    mg.Tangents.Add(v.Tangent);
                }

                foreach (var vertexId in p.TriangleIndices)
                {
                    // Have to bump these to account for merging the lists together.
                    mg.Indices.Add(vertCount + vertexId);
                }


                vertCount += p.Vertices.Count;
                indexCount += p.TriangleIndices.Count;
            }
            return mg;
        }

        private TTModel _Model;
        private List<ModelTextureData> _Textures;
        public TTModel Model {
            get => _Model;
        }
        public List<ModelTextureData> Textures {
            get => _Textures;
        }

        private List<MeshGeometry3D> _Geometry = new List<MeshGeometry3D>();
        private List<PhongMaterial> _Materials = new List<PhongMaterial>();

        /// <summary>
        /// Updates the model in the 3D viewport
        /// </summary>
        /// <param name="mdlData">The model data</param>
        /// <param name="textureDataDictionary">The texture dictionary for the model</param>
        public void UpdateModel(TTModel importModel = null, List<ModelTextureData> importTextures = null)
        {

            var newModel = false;
            var newTextures = false;
            var originalModel = _Model;

            if(importModel != null)
            {
                newModel = true;
                _Model = importModel;
            }
            var model = _Model;

            if (importTextures != null)
            { 
                newTextures = true;
                _Textures = importTextures;
            }
            var textureDataDictionary = _Textures;

            ClearModels();

            if (newModel && originalModel != _Model)
            {
                // Only recalculate if an actually new-new model, since this doesn't change on shape application.
                ModelModifiers.CalculateTangents(model);
            }

            if (newModel)
            {
                _Geometry.Clear();
                ModelModifiers.ApplyShapes(model, ActiveShapes, true);
            }

            if (newTextures)
            {
                _Materials.Clear();
            }
            
            SharpDX.BoundingBox? boundingBox = null;

            var totalMeshCount = model.MeshGroups.Count;



            for (var i = 0; i < totalMeshCount; i++)
            {
                if (newModel)
                {
                    var meshGeometry3D = GetMeshGeometry(model, i);
                    _Geometry.Add(meshGeometry3D);
                }

                if (newTextures)
                {

                    var isBodyMaterial = bodyMaterial.IsMatch(model.MeshGroups[i].Material);

                    var textureData = textureDataDictionary.FirstOrDefault(x => x.MaterialPath == model.MeshGroups[i].Material);
                    if (textureData == null)
                    {
                        // This material didn't exist, was corrupt or otherwise didn't get loaded.  Skip it.
                        continue;
                    }

                    TextureModel diffuse = null, specular = null, normal = null, alpha = null, emissive = null;
                    if (!isBodyMaterial)
                    {
                        if (textureData.Diffuse != null && textureData.Diffuse.Length > 0)
                            diffuse = new TextureModel(NormalizePixelData(textureData.Diffuse), textureData.Width, textureData.Height);

                        if (textureData.Specular != null && textureData.Specular.Length > 0)
                            specular = new TextureModel(NormalizePixelData(textureData.Specular), textureData.Width, textureData.Height);
                    }
                    else
                    {
                        if (textureData.Diffuse != null && textureData.Diffuse.Length > 0)
                            diffuse = new TextureModel(textureData.Diffuse, SharpDX.DXGI.Format.R8G8B8A8_UNorm, textureData.Width, textureData.Height);

                        if (textureData.Specular != null && textureData.Specular.Length > 0)
                            specular = new TextureModel(textureData.Specular, SharpDX.DXGI.Format.R8G8B8A8_UNorm, textureData.Width, textureData.Height);
                    }

                    if (textureData.Normal != null && textureData.Normal.Length > 0)
                        normal = new TextureModel(textureData.Normal, SharpDX.DXGI.Format.R8G8B8A8_UNorm, textureData.Width, textureData.Height);

                    if (textureData.Alpha != null && textureData.Alpha.Length > 0)
                        alpha = new TextureModel(textureData.Alpha, SharpDX.DXGI.Format.R8G8B8A8_UNorm, textureData.Width, textureData.Height);

                    if (textureData.Emissive != null && textureData.Emissive.Length > 0)
                        emissive = new TextureModel(textureData.Emissive, SharpDX.DXGI.Format.R8G8B8A8_UNorm, textureData.Width, textureData.Height);

                    var material = new PhongMaterial
                    {
                        DiffuseColor = PhongMaterials.ToColor(1, 1, 1, 1),
                        SpecularShininess = ReflectionValue,
                        DiffuseMap = diffuse,
                        DiffuseAlphaMap = alpha,
                        SpecularColorMap = specular,
                        NormalMap = normal,
                        EmissiveMap = emissive
                    };

                    _Materials.Add(material);
                }
                var mgm3d = new CustomMeshGeometryModel3D
                {
                    Geometry = _Geometry[i],
                    Material = _Materials[i],
                };
                boundingBox = _Geometry[i].Bound;

                mgm3d.CullMode = Properties.Settings.Default.Cull_Mode.Equals("None") ? CullMode.None : CullMode.Back;
                Models.Add(mgm3d);
            }



            var center = boundingBox.GetValueOrDefault().Center;

            _lightX = center.X;
            _lightY = center.Y;
            _lightZ = center.Z;

            Light3Direction = new Vector3D(_lightX, _lightY, _lightZ);
            Camera.UpDirection = new Vector3D(0, 1, 0);
            Camera.CameraInternal.PropertyChanged += CameraInternal_PropertyChanged;

            if(newModel && originalModel != _Model && !KeepCameraPosition)
            {
                ZoomExtentsRequested?.Invoke(this);
            }
        }

        /// <summary>
        /// Take the square root of every pixels' RGB datapoints to match the behavior of the FF14 engine
        /// </summary>
        /// <param name="img"></param>
        private static Color4[] NormalizePixelData(byte[] img)
        {
            Color4[] result = new Color4[img.Length / 4];
            Parallel.ForEach(Partitioner.Create(0, img.Length / 4), range =>
            {
                for (int i = range.Item1 * 4; i < range.Item2 * 4; i += 4)
                {
                    // This is the only way to do a true single-precision sqrt in .NET Framework
                    var tmp = Vector4.SquareRoot(new Vector4(
                        img[i] / 255.0f,
                        img[i + 1] / 255.0f,
                        img[i + 2] / 255.0f,
                        img[i + 3] / 255.0f
                    ));
                    result[i / 4] = new Color4(tmp.X, tmp.Y, tmp.Z, tmp.W);
                }
            });
            return result;
        }

        /// <summary>
        /// Event handler for the camera property changing
        /// </summary>
        protected virtual void CameraInternal_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName.Equals("LookDirection"))
            {
                var camera = sender as PerspectiveCameraCore;

                _light1X = -camera.LookDirection.X;
                _light1Y = -camera.LookDirection.Y;
                _light1Z = -camera.LookDirection.Z;


                Light1Direction = new Vector3D(_light1X, _light1Y, _light1Z);

                _light2X = camera.LookDirection.X;
                _light2Y = camera.LookDirection.Y;
                _light2Z = camera.LookDirection.Z;


                Light2Direction = new Vector3D(_light2X, _light2Y, _light2Z);
            }

            //ResetLightValues();
        }

        #region Properties

        /// <summary>
        /// The background color of the viewport
        /// </summary>
        public string BackgroundColor
        {
            get => _backgroundColor;
            set
            {
                _backgroundColor = value;
                OnPropertyChanged(nameof(BackgroundColor));
            }
        }

        /// <summary>
        /// The direction of light 1
        /// </summary>
        public Vector3D Light1Direction
        {
            get => _light1Direction;
            set
            {
                _light1Direction = value;
                OnPropertyChanged(nameof(Light1Direction));
            }
        }

        /// <summary>
        /// The direction of light 2
        /// </summary>
        public Vector3D Light2Direction
        {
            get => _light2Direction;
            set
            {
                _light2Direction = value;
                OnPropertyChanged(nameof(Light2Direction));
            }
        }

        /// <summary>
        /// The direction of light 3
        /// </summary>
        public Vector3D Light3Direction
        {
            get => _light3Direction;
            set
            {
                _light3Direction = value;
                OnPropertyChanged(nameof(Light3Direction));
            }
        }

        /// <summary>
        /// The render status of light 3
        /// </summary>
        public bool RenderLight3
        {
            get => _renderLight3;
            set
            {
                _renderLight3 = value;
                OnPropertyChanged(nameof(RenderLight3));
            }
        }

        #endregion

        /// <summary>
        /// Sets the visible flag for the selected mesh
        /// </summary>
        /// <param name="selectedMesh">The selected mesh</param>
        public void VisibleModels(string selectedMesh)
        {
            if (selectedMesh.Equals(XivStrings.All))
            {
                foreach (var model in Models)
                {
                    model.IsRendering = true;
                }
            }
            else
            {
                var modelNum = int.Parse(selectedMesh);

                for (var i = 0; i < Models.Count; i++)
                {
                    Models[i].IsRendering = (i == modelNum);
                }

            }
        }

        /// <summary>
        /// Clear all the models in the viewport
        /// </summary>
        public void ClearModels()
        {
            Models.Clear();
        }

        /// <summary>
        /// Updates the direction of the scene lights
        /// </summary>
        /// <param name="value">The new direction value</param>
        /// <param name="num">The light number</param>
        /// <param name="pos">The light position (X, Y, or Z)</param>
        public void UpdateLighting(double value, int num, string pos)
        {
            if (pos.Equals("X"))
            {
                if (num == 0)
                {
                    Light1Direction = new Vector3D(_light1X + value, Light1Direction.Y, Light1Direction.Z);
                }
                else if (num == 1)
                {
                    Light2Direction = new Vector3D(_light2X + value, Light2Direction.Y, Light2Direction.Z);
                }
                else if (num == 2)
                {
                    Light3Direction = new Vector3D(_lightX + value, Light3Direction.Y, Light3Direction.Z);
                }
            }

            if (pos.Equals("Y"))
            {
                if (num == 0)
                {
                    Light1Direction = new Vector3D(Light1Direction.X, _light1Y + value, Light1Direction.Z);
                }
                else if (num == 1)
                {
                    Light2Direction = new Vector3D(Light2Direction.X, _light2Y + value, Light2Direction.Z);
                }
                else if (num == 2)
                {
                    Light3Direction = new Vector3D(Light3Direction.X, _lightY + value, Light3Direction.Z);
                }
            }

            if (pos.Equals("Z"))
            {
                if (num == 0)
                {
                    Light1Direction = new Vector3D(Light1Direction.X, Light1Direction.Y, _light1Z + value);
                }
                else if (num == 1)
                {
                    Light2Direction = new Vector3D(Light2Direction.X, Light2Direction.Y, _light2Z + value);
                }
                else if (num == 2)
                {
                    Light3Direction = new Vector3D(Light3Direction.X, Light3Direction.Y, _lightZ + value);
                }
            }
        }

        /// <summary>
        /// Gets the current direction for a given light
        /// </summary>
        /// <param name="lightNumber">The light number</param>
        /// <returns></returns>
        public (double x, double y, double z) GetLightOffset(int lightNumber)
        {
            switch (lightNumber)
            {
                case 0:
                    {
                        var x = Light1Direction.X - _light1X;
                        var y = Light1Direction.Y - _light1Y;
                        var z = Light1Direction.Z - _light1Z;

                        return (x, y, z);
                    }
                case 1:
                    {
                        var x = Light2Direction.X - _light2X;
                        var y = Light2Direction.Y - _light2Y;
                        var z = Light2Direction.Z - _light2Z;

                        return (x, y, z);
                    }
                case 2:
                    {
                        var x = Light3Direction.X - _lightX;
                        var y = Light3Direction.Y - _lightY;
                        var z = Light3Direction.Z - _lightZ;

                        return (x, y, z);
                    }
                default:
                    return (0, 0, 0);
            }
        }

        /// <summary>
        /// Updates the reflection value of the model's material
        /// </summary>
        /// <param name="value">The new reflection value</param>
        public void UpdateReflection(int value)
        {
            foreach (var model in Models)
            {
                var material = ((MeshGeometryModel3D)model).Material as PhongMaterial;

                material.SpecularShininess = value;
            }

            _ReflectionValue = value;
        }

        /// <summary>
        /// Updates the transparency flag
        /// </summary>
        /// <remarks>
        /// This will make all models translucent,
        /// with the exception of any model which contains a body mesh
        /// </remarks>
        /// <param name="transparencyEnabled">The transparency enabled flag</param>
        public virtual void UpdateTransparency(bool transparencyEnabled)
        {
            foreach (var model in Models)
            {
                var isBody = ((CustomMeshGeometryModel3D)model).IsBody;

                if (isBody) continue;

                var material = ((CustomMeshGeometryModel3D)model).Material as PhongMaterial;

                if (transparencyEnabled)
                {
                    ((CustomMeshGeometryModel3D)model).IsTransparent = true;
                    material.DiffuseColor = PhongMaterials.ToColor(1, 1, 1, .4f);
                }
                else
                {
                    ((CustomMeshGeometryModel3D)model).IsTransparent = false;
                    material.DiffuseColor = PhongMaterials.ToColor(1, 1, 1, 1);
                }
            }
        }

        /// <summary>
        /// Updates the culling mode of the model
        /// </summary>
        /// <remarks>
        /// This will switch the cull mode to none if true, or back if false
        /// </remarks>
        /// <param name="noneCullMode">The None cull mode flag</param>
        public void UpdateCullMode(bool noneCullMode)
        {
            Settings.Default.Cull_Mode = noneCullMode ? UIStrings.None : UIStrings.Back;
            Settings.Default.Save();

            foreach (var model in Models)
            {
                ((MeshGeometryModel3D)model).CullMode = noneCullMode ? CullMode.None : CullMode.Back;
            }
        }

        #region INotify Properties


        private float _LightingXValue;
        public float LightingXValue
        {
            get => _LightingXValue;
            set
            {
                _LightingXValue = value;
                UpdateLighting(value, _CheckedLight, "X");
                LightXLabel = $"X  |  {value:0.#}";
                OnPropertyChanged(nameof(LightingXValue));
            }
        }

        public float _LightingYValue;
        public float LightingYValue
        {
            get => _LightingYValue;
            set
            {
                _LightingYValue = value;
                UpdateLighting(value, _CheckedLight, "Y");
                LightYLabel = $"Y  |  {value:0.#}";
                OnPropertyChanged(nameof(LightingYValue));
            }
        }

        public float _lightingZValue;
        public float LightingZValue
        {
            get => _lightingZValue;
            set
            {
                _lightingZValue = value;
                UpdateLighting(value, _CheckedLight, "Z");
                LightZLabel = $"Z  |  {value:0.#}";
                OnPropertyChanged(nameof(LightingZValue));
            }
        }

        public int _ReflectionValue  = 5;
        public int ReflectionValue
        {
            get => _ReflectionValue;
            set
            {
                UpdateReflection(value);
                ReflectionLabel = $"{UIStrings.Reflection}  |  {value}";
                OnPropertyChanged(nameof(ReflectionValue));
            }
        }

        private int _CheckedLight = 0;

        public bool _Light1Check = true;
        public bool Light1Check
        {
            get => _Light1Check;
            set
            {
                _Light1Check = value;
                if (value)
                {
                    _CheckedLight = 0;
                    UpdateLights();
                }
            }
        }

        public bool _Light2Check;
        public bool Light2Check
        {
            get => _Light2Check;
            set
            {
                _Light2Check = value;
                if (value)
                {
                    _CheckedLight = 1;
                    UpdateLights();
                }
            }
        }

        public bool _Light3Check;
        public bool Light3Check
        {
            get => _Light3Check;
            set
            {
                _Light3Check = value;
                if (value)
                {
                    _CheckedLight = 2;
                    UpdateLights();
                }
            }
        }

        public bool _LightRenderToggle;
        public bool LightRenderToggle
        {
            get => _LightRenderToggle;
            set
            {
                _LightRenderToggle = value;
                RenderLight3 = value;
                OnPropertyChanged(nameof(LightRenderToggle));
            }
        }

        public Visibility _LightToggleVisibility = Visibility.Collapsed;
        public Visibility LightToggleVisibility
        {
            get => _LightToggleVisibility;
            set
            {
                _LightToggleVisibility = value;
                OnPropertyChanged(nameof(LightToggleVisibility));
            }
        }

        public string _LightXLabel;
        public string LightXLabel
        {
            get => _LightXLabel;
            set
            {
                _LightXLabel = value;
                OnPropertyChanged(nameof(LightXLabel));
            }
        }

        public string _LightYLabel;
        public string LightYLabel
        {
            get => _LightYLabel;
            set
            {
                _LightYLabel = value;
                OnPropertyChanged(nameof(LightYLabel));
            }
        }

        public string _LightZLabel;
        public string LightZLabel
        {
            get => _LightZLabel;
            set
            {
                _LightZLabel = value;
                OnPropertyChanged(nameof(LightZLabel));
            }
        }

        public string _reflectionLabel;
        public string ReflectionLabel
        {
            get => _reflectionLabel;
            set
            {
                _reflectionLabel = value;
                OnPropertyChanged(nameof(ReflectionLabel));
            }
        }

        public bool _TransparencyToggle;
        public bool TransparencyToggle
        {
            get => _TransparencyToggle;
            set
            {
                _TransparencyToggle = value;
                UpdateTransparency(value);
                OnPropertyChanged(nameof(TransparencyToggle));
            }
        }

        private bool _CullModeToggle;
        public bool CullModeToggle
        {
            get => _CullModeToggle;
            set
            {
                _CullModeToggle = value;
                UpdateCullMode(value);
                OnPropertyChanged(nameof(CullModeToggle));
            }
        }

        public bool _KeepCameraPosition;
        public bool KeepCameraPosition
        {
            get => _KeepCameraPosition;
            set
            {
                _KeepCameraPosition = value;
                OnPropertyChanged(nameof(KeepCameraPosition));
            }
        }


        private int _HighlightedColorsetRow = -1;
        public int HighlightedColorsetRow
        {
            get => _HighlightedColorsetRow;
            set
            {
                _HighlightedColorsetRow = value;
                OnPropertyChanged(nameof(HighlightedColorsetRow));
                
                // This has to be handled by our external view.
                TextureUpdateRequested?.Invoke(this);
            }
        }

        private List<string> _ActiveShapes = new List<string>();

        public List<string> ActiveShapes
        {
            get => _ActiveShapes.ToList();
        }

        public void AddShape(string shape)
        {
            _ActiveShapes.Add(shape);
            UpdateModel(_Model);
        }

        public void RemoveShape(string shape)
        {
            _ActiveShapes.Remove(shape);
            UpdateModel(_Model);
        }
        
        public void SetShapes(IEnumerable<string> shapes)
        {
            _ActiveShapes = shapes.ToList();
            UpdateModel(_Model);
        }


        /// <summary>
        /// Update the light position values on the sliders
        /// </summary>
        private void UpdateLights()
        {
            var lightValues = GetLightOffset(_CheckedLight);

            LightingXValue = (float)lightValues.x;
            LightXLabel = $"X  |  {lightValues.x:0.#}";
            LightingYValue = (float)lightValues.y;
            LightYLabel = $"Y  |  {lightValues.y:0.#}";
            LightingZValue = (float)lightValues.z;
            LightZLabel = $"Z  |  {lightValues.z:0.#}";

            LightToggleVisibility = _CheckedLight == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Resets the light position values for the sliders
        /// </summary>
        public void ResetLightValues()
        {
            if (_CheckedLight == 2) return;

            LightingXValue = 0;
            LightXLabel = "X  |  0";
            LightingYValue = 0;
            LightYLabel = "Y  |  0";
            LightingZValue = 0;
            LightZLabel = "Z  |  0";
        }

        #endregion
    }
}

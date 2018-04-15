﻿/*****************************************************************************
 * The MIT License (MIT)
 * 
 * Copyright (c) 2018 MOARdV
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * 
 ****************************************************************************/
using System;
using UnityEngine;

namespace AvionicsSystems
{
    class MASPageOrbitDisplay : IMASMonitorComponent
    {
        private string name = "anonymous";

        private GameObject imageObject;
        private GameObject cameraObject;
        private MeshRenderer rentexRenderer;
        private Material imageMaterial;
        private RenderTexture displayRenTex;
        private Camera orbitCamera;
        private static readonly int orbitLayer = 29;

        // 1/2 width and 1/2 height.
        private Vector2 size;
        private VariableRegistrar variableRegistrar;
        private MASFlightComputer comp;

        private readonly int vertexCount;
        private Vector3[] vesselVertices;
        private GameObject vesselOrigin;
        private LineRenderer vesselRenderer;
        private KeplerianElements vesselOrbit = new KeplerianElements();
        private Color vesselStartColor = XKCDColors.White;
        private Color vesselEndColor = XKCDColors.White;

        internal static readonly float maxDepth = 1.0f - depthDelta;
        internal static readonly float minDepth = 0.5f;
        internal static readonly float depthDelta = 1.0f / 256.0f;

        internal MASPageOrbitDisplay(ConfigNode config, InternalProp prop, MASFlightComputer comp, MASMonitor monitor, Transform pageRoot, float depth)
        {
            variableRegistrar = new VariableRegistrar(comp, prop);
            this.comp = comp;

            if (!config.TryGetValue("name", ref name))
            {
                name = "anonymous";
            }

            Vector2 position = Vector2.zero;
            if (!config.TryGetValue("position", ref position))
            {
                throw new ArgumentException("Unable to find 'position' in ORBIT_DISPLAY " + name);
            }

            size = Vector2.zero;
            if (!config.TryGetValue("size", ref size))
            {
                throw new ArgumentException("Unable to find 'size' in ORBIT_DISPLAY " + name);
            }
            if (Mathf.Approximately(size.x, 0.0f) || Mathf.Approximately(size.y, 0.0f))
            {
                throw new ArgumentException("Invalid 'size' in ORBIT_DISPLAY " + name);
            }

            float orbitWidth = 1.0f;
            if (!config.TryGetValue("orbitWidth", ref orbitWidth))
            {
                orbitWidth = 1.0f;
            }

            if (!config.TryGetValue("vertexCount", ref vertexCount))
            {
                throw new ArgumentException("Unable to find 'vertexCount' in ORBIT_DISPLAY " + name);
            }
            if (vertexCount < 3)
            {
                throw new ArgumentException("'vertexCount' needs to be at least 3 in ORBIT_DISPLAY " + name);
            }
            vesselVertices = new Vector3[vertexCount];

            // Set up our display surface.
            Shader displayShader = Shader.Find("KSP/Alpha/Unlit Transparent");

            int renTexX = (int)size.x;
            Utility.LastPowerOf2(ref renTexX, 64, 1024);
            int renTexY = (int)size.y;
            Utility.LastPowerOf2(ref renTexY, 64, 1024);

            // Need 1/2 sizes for the rest of this:
            size = size * 0.5f;

            displayRenTex = new RenderTexture(renTexX, renTexY, 24, RenderTextureFormat.ARGB32);
            displayRenTex.Create();
            displayRenTex.DiscardContents();

            imageObject = new GameObject();
            imageObject.name = Utility.ComposeObjectName(pageRoot.gameObject.name, this.GetType().Name, name, (int)(-depth / MASMonitor.depthDelta));
            imageObject.layer = pageRoot.gameObject.layer;
            imageObject.transform.parent = pageRoot;
            imageObject.transform.position = pageRoot.position;
            imageObject.transform.Translate(monitor.screenSize.x * -0.5f + position.x + size.x, monitor.screenSize.y * 0.5f - position.y - size.y, depth);
            // add renderer stuff
            MeshFilter meshFilter = imageObject.AddComponent<MeshFilter>();
            rentexRenderer = imageObject.AddComponent<MeshRenderer>();
            Mesh mesh = new Mesh();
            mesh.vertices = new[]
                {
                    new Vector3(-size.x, size.y, 0.0f),
                    new Vector3(size.x, size.y, 0.0f),
                    new Vector3(-size.x, -size.y, 0.0f),
                    new Vector3(size.x, -size.y, 0.0f),
                };
            mesh.uv = new[]
                {
                    new Vector2(0.0f, 1.0f),
                    Vector2.one,
                    Vector2.zero,
                    new Vector2(1.0f, 0.0f),
                };
            mesh.triangles = new[] 
                {
                    0, 1, 2,
                    1, 3, 2
                };
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            meshFilter.mesh = mesh;
            imageMaterial = new Material(displayShader);
            imageMaterial.mainTexture = displayRenTex;
            rentexRenderer.material = imageMaterial;

            //cameraObject
            cameraObject = new GameObject();
            cameraObject.name = Utility.ComposeObjectName(pageRoot.gameObject.name, this.GetType().Name, name + "-Camera", (int)(-depth / MASMonitor.depthDelta));
            cameraObject.layer = pageRoot.gameObject.layer;
            cameraObject.transform.parent = pageRoot;
            cameraObject.transform.position = pageRoot.position;
            orbitCamera = cameraObject.AddComponent<Camera>();
            orbitCamera.enabled = true;
            orbitCamera.orthographic = true;
            orbitCamera.aspect = size.x / size.y;
            orbitCamera.eventMask = 0;
            orbitCamera.farClipPlane = 1.0f + depthDelta;
            orbitCamera.nearClipPlane = depthDelta;
            orbitCamera.orthographicSize = size.y;
            orbitCamera.cullingMask = 1 << orbitLayer;
            //orbitCamera.backgroundColor = new Color(0.0f, 0.0f, 0.4f, 1.0f);
            orbitCamera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
            orbitCamera.clearFlags = CameraClearFlags.SolidColor;
            orbitCamera.transparencySortMode = TransparencySortMode.Orthographic;
            orbitCamera.transform.position = Vector3.zero;
            orbitCamera.transform.LookAt(new Vector3(0.0f, 0.0f, maxDepth), Vector3.up);
            orbitCamera.targetTexture = displayRenTex;

            // lineRenderers
            float lineDepth = 1.0f;

            // vessel orbit
            vesselOrigin = new GameObject();
            vesselOrigin.name = Utility.ComposeObjectName(pageRoot.gameObject.name, this.GetType().Name, name, (int)(-lineDepth / MASMonitor.depthDelta));
            vesselOrigin.layer = orbitLayer;// pageRoot.gameObject.layer;
            vesselOrigin.transform.parent = cameraObject.transform;
            vesselOrigin.transform.position = cameraObject.transform.position;
            vesselOrigin.transform.Translate(0.0f, 0.0f, lineDepth);
            //vesselOrigin.transform.Translate(monitor.screenSize.x * -0.5f + position.x, monitor.screenSize.y * 0.5f - position.y, depth);
            // add renderer stuff
            //lineMaterial = new Material(MASLoader.shaders["MOARdV/Monitor"]);
            vesselRenderer = vesselOrigin.AddComponent<LineRenderer>();
            vesselRenderer.useWorldSpace = false;
            vesselRenderer.material = new Material(MASLoader.shaders["MOARdV/Monitor"]);
            vesselRenderer.startColor = vesselStartColor;
            vesselRenderer.endColor = vesselEndColor;
            vesselRenderer.startWidth = 4.0f;
            vesselRenderer.endWidth = 4.0f;
            vesselRenderer.positionCount = vertexCount;
            vesselRenderer.loop = true;
            lineDepth -= 0.0625f;

            // Load the colors.
            InitVesselColor(comp, config);

            Camera.onPreCull += CameraPrerender;
            Camera.onPostRender += CameraPostrender;

            //UpdateVertices();
        }

        private void InitVesselColor(MASFlightComputer comp, ConfigNode config)
        {
            string vesselStartColorString = string.Empty;
            string vesselEndColorString = string.Empty;
            config.TryGetValue("vesselEndColor", ref vesselEndColorString);

            if (config.TryGetValue("vesselStartColor", ref vesselStartColorString))
            {
                if (string.IsNullOrEmpty(vesselEndColorString))
                {
                    Color32 color;
                    if (comp.TryGetNamedColor(vesselStartColorString, out color))
                    {
                        vesselStartColor = color;
                        vesselRenderer.startColor = vesselStartColor;
                        vesselRenderer.endColor = vesselStartColor;
                    }
                    else
                    {
                        string[] vesselColors = Utility.SplitVariableList(vesselStartColorString);
                        if (vesselColors.Length < 3 || vesselColors.Length > 4)
                        {
                            throw new ArgumentException("vesselStartColor does not contain 3 or 4 values in ORBIT_DISPLAY " + name);
                        }

                        variableRegistrar.RegisterNumericVariable(vesselColors[0], (double newValue) =>
                        {
                            vesselStartColor.r = Mathf.Clamp01((float)newValue * (1.0f / 255.0f));
                            vesselRenderer.startColor = vesselStartColor;
                            vesselRenderer.endColor = vesselStartColor;
                        });
                        variableRegistrar.RegisterNumericVariable(vesselColors[1], (double newValue) =>
                        {
                            vesselStartColor.g = Mathf.Clamp01((float)newValue * (1.0f / 255.0f));
                            vesselRenderer.startColor = vesselStartColor;
                            vesselRenderer.endColor = vesselStartColor;
                        });
                        variableRegistrar.RegisterNumericVariable(vesselColors[2], (double newValue) =>
                        {
                            vesselStartColor.b = Mathf.Clamp01((float)newValue * (1.0f / 255.0f));
                            vesselRenderer.startColor = vesselStartColor;
                            vesselRenderer.endColor = vesselStartColor;
                        });

                        if (vesselColors.Length == 4)
                        {
                            variableRegistrar.RegisterNumericVariable(vesselColors[3], (double newValue) =>
                            {
                                vesselStartColor.a = Mathf.Clamp01((float)newValue * (1.0f / 255.0f));
                                vesselRenderer.startColor = vesselStartColor;
                                vesselRenderer.endColor = vesselStartColor;
                            });
                        }
                    }
                }
                else
                {
                    Color32 color;
                    if (comp.TryGetNamedColor(vesselStartColorString, out color))
                    {
                        vesselStartColor = color;
                        vesselRenderer.startColor = vesselStartColor;
                    }
                    else
                    {
                        string[] vesselColors = Utility.SplitVariableList(vesselStartColorString);
                        if (vesselColors.Length < 3 || vesselColors.Length > 4)
                        {
                            throw new ArgumentException("vesselStartColor does not contain 3 or 4 values in ORBIT_DISPLAY " + name);
                        }

                        variableRegistrar.RegisterNumericVariable(vesselColors[0], (double newValue) =>
                        {
                            vesselStartColor.r = Mathf.Clamp01((float)newValue * (1.0f / 255.0f));
                            vesselRenderer.startColor = vesselStartColor;
                        });
                        variableRegistrar.RegisterNumericVariable(vesselColors[1], (double newValue) =>
                        {
                            vesselStartColor.g = Mathf.Clamp01((float)newValue * (1.0f / 255.0f));
                            vesselRenderer.startColor = vesselStartColor;
                        });
                        variableRegistrar.RegisterNumericVariable(vesselColors[2], (double newValue) =>
                        {
                            vesselStartColor.b = Mathf.Clamp01((float)newValue * (1.0f / 255.0f));
                            vesselRenderer.startColor = vesselStartColor;
                        });

                        if (vesselColors.Length == 4)
                        {
                            variableRegistrar.RegisterNumericVariable(vesselColors[3], (double newValue) =>
                            {
                                vesselStartColor.a = Mathf.Clamp01((float)newValue * (1.0f / 255.0f));
                                vesselRenderer.startColor = vesselStartColor;
                            });
                        }
                    }

                    if (comp.TryGetNamedColor(vesselEndColorString, out color))
                    {
                        vesselEndColor = color;
                        vesselRenderer.endColor = vesselEndColor;
                    }
                    else
                    {
                        string[] vesselColors = Utility.SplitVariableList(vesselEndColorString);
                        if (vesselColors.Length < 3 || vesselColors.Length > 4)
                        {
                            throw new ArgumentException("vesselEndColor does not contain 3 or 4 values in ORBIT_DISPLAY " + name);
                        }

                        variableRegistrar.RegisterNumericVariable(vesselColors[0], (double newValue) =>
                        {
                            vesselEndColor.r = Mathf.Clamp01((float)newValue * (1.0f / 255.0f));
                            vesselRenderer.endColor = vesselEndColor;
                        });
                        variableRegistrar.RegisterNumericVariable(vesselColors[1], (double newValue) =>
                        {
                            vesselEndColor.g = Mathf.Clamp01((float)newValue * (1.0f / 255.0f));
                            vesselRenderer.endColor = vesselEndColor;
                        });
                        variableRegistrar.RegisterNumericVariable(vesselColors[2], (double newValue) =>
                        {
                            vesselEndColor.b = Mathf.Clamp01((float)newValue * (1.0f / 255.0f));
                            vesselRenderer.endColor = vesselEndColor;
                        });

                        if (vesselColors.Length == 4)
                        {
                            variableRegistrar.RegisterNumericVariable(vesselColors[3], (double newValue) =>
                            {
                                vesselEndColor.a = Mathf.Clamp01((float)newValue * (1.0f / 255.0f));
                                vesselRenderer.endColor = vesselEndColor;
                            });
                        }
                    }
                }
            }
            else if(!string.IsNullOrEmpty(vesselEndColorString))
            {
                throw new ArgumentException("vesselEndColor found, but no vesselStartColor in ORBIT_DISPLAY " + name);
            }
        }

        private void GenerateEllipse(ref Vector3[] verts, float radiusX, float radiusY, float startTheta, float endTheta)
        {
            float theta = 0.0f;
            float radiansPerVertex = (endTheta - startTheta) / (float)vertexCount;
            for (int i = 0; i < vertexCount; ++i)
            {
                float sinTheta = Mathf.Sin(theta + startTheta);
                float cosTheta = Mathf.Cos(theta + startTheta);
                theta += radiansPerVertex;

                verts[i].x = radiusX * cosTheta;
                verts[i].y = radiusY * sinTheta;
                verts[i].z = 0.0f;
            }
        }

        private bool OrbitsMatch(ref KeplerianElements lastValue, Orbit orbit)
        {
            bool match = true;
            if (Math.Abs(lastValue.eccentricity - orbit.eccentricity) > 0.001)
            {
                match = false;
                lastValue.eccentricity = orbit.eccentricity;
            }
            if (Math.Abs(lastValue.semiMajorAxis - orbit.semiMajorAxis) > 10.0) // 10 m tolerance  Probably safe to use a larger value
            {
                match = false;
                lastValue.semiMajorAxis = orbit.semiMajorAxis;
            }
            if (Math.Abs(lastValue.inclination - orbit.inclination) > 1.0) // degrees
            {
                match = false;
                lastValue.inclination = orbit.inclination;
            }
            if (Math.Abs(lastValue.LAN - orbit.LAN) > 1.0) // degrees
            {
                match = false;
                lastValue.LAN = orbit.LAN;
            }
            if (Math.Abs(lastValue.argumentOfPeriapsis - orbit.argumentOfPeriapsis) > 1.0) // degrees
            {
                match = false;
                lastValue.argumentOfPeriapsis = orbit.argumentOfPeriapsis;
            }
            if (Math.Abs(lastValue.trueAnomaly - orbit.trueAnomaly) > (Math.PI / 128.0)) // units of radians
            {
                match = false;
                lastValue.trueAnomaly = orbit.trueAnomaly;
            }
            lastValue.semiMinorAxis = orbit.semiMinorAxis;

            return match;
        }

        /// <summary>
        /// Compute the scaling factor that will fit the desired orbits onto the screen.
        /// </summary>
        /// <returns></returns>
        private double ComputeScaling()
        {
            double xLimit = size.x / 1.01;
            double yLimit = size.y / 1.01;

            if (double.IsNaN(vesselOrbit.semiMajorAxis))
            {
                return 0.0;
            }

            double metersToPixels = xLimit / vesselOrbit.semiMajorAxis;
            metersToPixels = Math.Min(metersToPixels, yLimit / vesselOrbit.semiMinorAxis);

            return metersToPixels;
        }

        /// <summary>
        /// Update all of the vertices.
        /// </summary>
        private void UpdateVertices()
        {
            bool invalidateVertices = false;

            // Check components for required updates
            if (!OrbitsMatch(ref vesselOrbit, comp.vessel.GetOrbit()))
            {
                invalidateVertices = true;
            }

            if (invalidateVertices)
            {
                double metersToPixels = ComputeScaling();

                // TODO: Hyperbolic orbits.
                // TODO: Orbits with a start and/or end time.
                if (vesselOrbit.eccentricity < 1.0f)
                {
                    //  if (no start time && no end time)
                    float radiusX = (float)(vesselOrbit.semiMajorAxis * metersToPixels);
                    float radiusY = (float)(vesselOrbit.semiMinorAxis * metersToPixels);

                    // True Anomaly of 0 is where the vessel crosses the periapsis.  Since we're putting the
                    // periapsis on the left, but we're leaving the winding of the ellipse computation alone,
                    // we subtract pi from the TA.
                    GenerateEllipse(ref vesselVertices, radiusX, radiusY, (float)vesselOrbit.trueAnomaly - Mathf.PI, (float)vesselOrbit.trueAnomaly + Mathf.PI);
                    //  {
                    //  else generate limited ellipse
                    //  {
                    //  }
                }
                // else generate hyperbolic section

                vesselRenderer.SetPositions(vesselVertices);
            }
        }

        /// <summary>
        /// Callback called before culling - we use this to update marker positions
        /// and determine which markers (and the navball) should be rendered.
        /// </summary>
        /// <param name="whichCamera"></param>
        private void CameraPrerender(Camera whichCamera)
        {
            if (whichCamera == orbitCamera)
            {
                UpdateVertices();

                rentexRenderer.enabled = true;
                vesselRenderer.enabled = true;
                if (!displayRenTex.IsCreated())
                {
                    displayRenTex.Create();
                    orbitCamera.targetTexture = displayRenTex;
                    imageMaterial.mainTexture = displayRenTex;
                }

            }
        }

        /// <summary>
        /// Switch off our renderers once the navball camera is done.
        /// </summary>
        /// <param name="whichCamera"></param>
        private void CameraPostrender(Camera whichCamera)
        {
            if (whichCamera == orbitCamera)
            {
                rentexRenderer.enabled = false;
                vesselRenderer.enabled = false;
            }
        }

        /// <summary>
        /// Enable / disable renderer components without disabling game objects.
        /// </summary>
        /// <param name="enable"></param>
        public void EnableRender(bool enable)
        {
            rentexRenderer.enabled = enable;
            vesselRenderer.enabled = enable;
        }

        /// <summary>
        /// Enables / disables overall page rendering.
        /// </summary>
        /// <param name="enable"></param>
        public void EnablePage(bool enable)
        {

        }

        /// <summary>
        /// Handle a softkey event.
        /// </summary>
        /// <param name="keyId">The numeric ID of the key to handle.</param>
        /// <returns>true if the component handled the key, false otherwise.</returns>
        public bool HandleSoftkey(int keyId)
        {
            return false;
        }

        /// <summary>
        ///  Return the name of the action.
        /// </summary>
        /// <returns></returns>
        public string Name()
        {
            return name;
        }

        /// <summary>
        /// Release resources
        /// </summary>
        public void ReleaseResources(MASFlightComputer comp, InternalProp internalProp)
        {
            Camera.onPreCull -= CameraPrerender;
            Camera.onPostRender -= CameraPostrender;

            this.comp = null;

            UnityEngine.GameObject.Destroy(imageObject);
            imageObject = null;
            UnityEngine.Object.Destroy(imageMaterial);
            imageMaterial = null;

            UnityEngine.GameObject.Destroy(cameraObject);
            cameraObject = null;

            variableRegistrar.ReleaseResources(comp, internalProp);
        }

        /// <summary>
        /// Describes the parameters of an orbit.  Cache values so we can see if an orbit has changed,
        /// which would trigger the need to update our orbit display.
        /// </summary>
        struct KeplerianElements
        {
            public double eccentricity;
            public double semiMajorAxis;
            public double inclination;
            public double LAN;
            public double argumentOfPeriapsis;
            public double trueAnomaly; // This is the position of the object, but not important to drawing the orbital ellipse.
            public double semiMinorAxis; // Not important to drawing, but handy to avoid computing it later.
        }
    }
}
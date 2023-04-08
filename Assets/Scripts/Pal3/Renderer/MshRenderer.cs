﻿using System.Collections.Generic;
using System.Linq;
using Core.GameBox;
using UnityEngine.InputSystem.XR;
using UnityEngine.UIElements;
using Bone = UnityEngine.XR.Bone;

namespace Pal3.Renderer
{
    using System;
    using Core.DataReader.Msh;
    using Core.DataReader.Mov;
    using UnityEngine;
    
    class Joint
    {
        public string name;
        public GameObject go = null;
        public BoneNode boneNode = null;
        public BoneActTrack track = null;
        public Matrix4x4 cacheTransform = Matrix4x4.identity;
        
        public void Clear()
        {
            track = null;
            cacheTransform = Matrix4x4.identity;
        }
    }

    /// <summary>
    /// MSH(.msh) model renderer
    /// </summary>
    public class MshRenderer : MonoBehaviour, IDisposable
    {
        private IMaterialFactory _materialFactory;
        private Material[][] _materials;

        private MshFile _msh = null;

        private MovFile _mov = null;
        private float _elapsedTime = 0.0f;
        
        //private Dictionary<string, Joint> _jointDict = new Dictionary<string, Joint>();
        private Dictionary<int, Joint> _jointDict = new Dictionary<int, Joint>();

        private Material[] _skinningMaterials = null;

        private const int MAX_BONES_CNT = 50;

        public void Init(MshFile mshFile,
            IMaterialFactory materialFactory)
        {
            _msh = mshFile;
            _materialFactory = materialFactory;

            RenderSkeleton();
            CheckSkeleton();
            RenderMesh();
        }

        public void PlayMov(MovFile mov)
        {
            _mov = mov;
            ClearJointTrack();
            BindJointTrack();
        }

        private void ClearJointTrack()
        {
            foreach (var joint in _jointDict)
            {
                joint.Value.Clear();
            }
        }

        private void BindJointTrack()
        {
            for (int i = 0; i < _mov.boneTrackArray.Length; i++)
            {
                var track = _mov.boneTrackArray[i];
                if(_jointDict.ContainsKey(track.boneId))
                {
                    var joint = _jointDict[track.boneId];
                    joint.track = track;
                }
            }
        }

        private void Update()
        {
            if (_mov != null)
            {
                // Get Tick Index
                _elapsedTime += Time.deltaTime;
                int tickIdx = GameBoxInterpreter.SecondToTick(_elapsedTime);
                if (tickIdx >= _mov.nDuration)
                {
                    _elapsedTime = 0;
                    tickIdx = 0;
                }
                //Debug.Log("Tick Index:" + tickIdx);
                UpdateAllJoints(tickIdx);
                UpdateSkinning();
            }
        }

        private void UpdateAllJoints(int tickIdx)
        {
            UpdateJoint(_jointDict[0],tickIdx);
        }
        
        private void UpdateJoint(Joint joint,int tickIdx)
        {
            GameObject go = joint.go;
            BoneActTrack track = joint.track;
            BoneNode bone = joint.boneNode;

            if (track != null)
            {
                int keyIdx = GetKeyIndex(track, tickIdx);
                Vector3 trans = track.keyArray[keyIdx].trans;
                Quaternion rot = track.keyArray[keyIdx].rot;

                go.transform.localPosition = trans;
                go.transform.localRotation = rot;
                
                // @miao @todo
                // update matrix here 
                if (bone._parentID >= 0)
                {
                    var parentJoint = _jointDict[bone._parentID];
                    
                    Matrix4x4 translateMatrix = Matrix4x4.Translate(go.transform.localPosition);
                    Matrix4x4 rotateMatrix = Matrix4x4.Rotate(go.transform.localRotation);
                    joint.cacheTransform = parentJoint.cacheTransform * rotateMatrix * translateMatrix;
                    //joint.cacheTransform = rotateMatrix * translateMatrix;
                }
                else
                {
                    joint.cacheTransform = Matrix4x4.identity;
                }
            }
            
            // update children
            for(int i = 0;i < bone._nChildren;i++)
            {
                int childJointIdx = bone._children[i]._selfID;
                var childJoint = _jointDict[childJointIdx];
                UpdateJoint(childJoint,tickIdx);
            }
        }

        private int GetKeyIndex(BoneActTrack track, int tickIdx)
        {
            int keyIdx = 0;
            for (keyIdx = 0; keyIdx < track.nKey; keyIdx++)
            {
                if (tickIdx < track.keyArray[keyIdx].time)
                {
                    break;
                }
            }

            if (keyIdx == 0 || keyIdx >= track.nKey)
            {
                keyIdx = track.nKey - 1;
            }

            return keyIdx;
        }
        
        void UpdateSkinning()
        {
            // Here we should pass bones matrix to uniform
            List<Matrix4x4> boneMatrixArray = new List<Matrix4x4>();
            for (int i = 0; i < MAX_BONES_CNT; i++)
            {
                if (_jointDict.ContainsKey(i))
                {
                    // Here we should get the joint , and calc the matrix 
                    var joint = _jointDict[i];
                    boneMatrixArray.Add(joint.cacheTransform);
                }
                else
                {
                    boneMatrixArray.Add(Matrix4x4.identity);
                }

            }

            foreach (var material in _skinningMaterials)
            {
                material.SetMatrixArray(Shader.PropertyToID("_boneMatrixArray"), boneMatrixArray);
            }
        }

        void RenderSkeleton()
        {
            RenderBone(_msh._boneRoot, gameObject,null);
        }

        void RenderBone(BoneNode bone, GameObject parent,BoneNode parentBone)
        {
            GameObject renderNode = new GameObject();
            renderNode.name = $"[bone][name]{bone._name} [id]{bone._selfID}";
            renderNode.transform.SetParent(parent.transform);

            // display gizmo
            RenderBoneGizmo(renderNode);
            RenderBoneConnectionGizmo(parent.transform, renderNode.transform);

            // self pos rotation
            renderNode.transform.localPosition = bone._translate;
            renderNode.transform.localRotation = bone._rotate;

            // children
            for (int i = 0; i < bone._nChildren; i++)
            {
                BoneNode subBone = bone._children[i];
                RenderBone(subBone, renderNode,bone);
            }
            
            // hold joint to dict
            var joint = new Joint();
            joint.go = renderNode;
            joint.boneNode = bone;
            joint.track = null;
            if(parentBone != null)
            {
                _jointDict.Add(bone._selfID,joint);
            }            
        }

        private void RenderBoneGizmo(GameObject boneRenderNode)
        {
            var meshFilter = boneRenderNode.AddComponent<MeshFilter>();
            var meshRenderer = boneRenderNode.AddComponent<MeshRenderer>();

            var mesh = new UnityEngine.Mesh();
            mesh.SetVertices(BuildBoneGizmoMesh());
            mesh.SetTriangles(BuildBoneGizmoTriangle(), 0);
            meshFilter.sharedMesh = mesh;

            // material
            Material material = _materialFactory.CreateBoneGizmoMaterial();
            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
                material.renderQueue = 5000; // at last 
            }
        }

        void RenderBoneConnectionGizmo(Transform from, Transform to)
        {
            //Vector3 fromTo = to.position - from.position;
        }

        void RenderMesh()
        {
            if (_msh._nSubMesh > 0)
            {
                _skinningMaterials = new Material[_msh._nSubMesh];
            }

            for (int i = 0; i < _msh._nSubMesh; i++)
            {
                SubMesh subMesh = _msh._subMeshArray[i];
                RenderSubMesh(subMesh, i);
            }
        }
    
        void CheckSkeleton()
        {
            int n = GetBoneCount();
            Debug.Assert(n <= MAX_BONES_CNT,"bone number more than MAX_BONE_CNT！");
            Debug.Assert(n > 0,"There's no joint!");
            for (int i = 0;i < n;i++)
            {
                Debug.Assert(_jointDict.ContainsKey(i),"Bone index missing:" + i);
            }
        }
        
        int GetBoneCount()
        {
            return _jointDict.Count;
        }

        void RenderSubMesh(SubMesh subMesh, int subMeshIndex)
        {
            GameObject subMeshNode = new GameObject($"[submesh] {subMeshIndex}");
            subMeshNode.transform.SetParent(gameObject.transform);

            var meshRenderer = subMeshNode.AddComponent<MeshRenderer>();
            var meshFilter = subMeshNode.AddComponent<MeshFilter>();

            var mesh = new UnityEngine.Mesh();
            mesh.MarkDynamic(); // @temp
            mesh.SetVertices(BuildVerts(subMesh));
            mesh.SetTriangles(BuildTriangles(subMesh), subMeshIndex);

            mesh.SetUVs(1, BuildBoneIds(subMesh));
            mesh.SetUVs(2, BuildBoneWeights(subMesh));

            meshFilter.sharedMesh = mesh;

            // hold material
            var material = _materialFactory.CreateSkinningMaterial();
            meshRenderer.sharedMaterial = material;
            _skinningMaterials[subMeshIndex] = material;
        }

        Vector3[] BuildVerts(SubMesh subMesh)
        {
            Vector3[] verts = new Vector3[subMesh._verts.Length];
            for (int i = 0; i < subMesh._verts.Length; i++)
            {
                verts[i] = subMesh._verts[i].pos;
            }

            return verts;
        }

        int[] BuildTriangles(SubMesh subMesh)
        {
            int len = subMesh._faces.Length * 3;
            int[] triangles = new int[len];

            int idx = 0;
            for (int i = 0; i < subMesh._faces.Length; i++)
            {
                triangles[idx++] = subMesh._faces[i].vertIndex[0];
                triangles[idx++] = subMesh._faces[i].vertIndex[1];
                triangles[idx++] = subMesh._faces[i].vertIndex[2];
            }

            return triangles;
        }

        List<Vector4> BuildBoneWeights(SubMesh subMesh)
        {
            List<Vector4> weights = new List<Vector4>();
            for (int i = 0; i < subMesh._verts.Length; i++)
            {
                var vert = subMesh._verts[i];
                
                float weightSum = vert.weights[0] +  vert.weights[1] + vert.weights[2]+vert.weights[3];
                Vector4 weightsNormalize = new Vector4(vert.weights[0],vert.weights[1],vert.weights[2],vert.weights[3]);
                weightsNormalize = weightsNormalize / weightSum;
                var weight = weightsNormalize;
                weights.Add(weight);
            }
            //Debug.Log("weights:" + weights);
            return weights;
        }

        List<Vector4> BuildBoneIds(SubMesh subMesh)
        {
            List<Vector4> ids = new List<Vector4>();
            for (int i = 0; i < subMesh._verts.Length; i++)
            {
                var vert = subMesh._verts[i];
                var boneIds = new Vector4(vert.boneIds[0], vert.boneIds[1], vert.boneIds[2], vert.boneIds[3]);
                ids.Add(boneIds);
            }

            return ids;
        }

        Vector3[] BuildBoneGizmoMesh()
        {
            const float size = 0.02f;
            Vector3[] verts = new Vector3[8];
            verts[0] = new Vector3(-size, -size, -size);
            verts[1] = new Vector3(-size, size, -size);
            verts[2] = new Vector3(size, -size, -size);
            verts[3] = new Vector3(size, size, -size);

            verts[4] = new Vector3(-size, -size, size);
            verts[5] = new Vector3(-size, size, size);
            verts[6] = new Vector3(size, -size, size);
            verts[7] = new Vector3(size, -size, size);

            return verts;
        }

        int[] BuildBoneGizmoTriangle()
        {
            int[] indices =
            {
                0, 1, 2, 2, 1, 3,
                4, 5, 6, 6, 5, 7,

                4, 5, 0, 0, 5, 1,
                2, 4, 6, 6, 4, 7,

                1, 5, 3, 3, 5, 7,
                0, 4, 2, 2, 4, 6,
            };
            return indices;
        }

        private void OnDisable()
        {
            Dispose();
        }

        public void Dispose()
        {

        }
    }
}
/*
 * Copyright (c) Contributors, http://aurora-sim.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Aurora-Sim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Threading;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Region.Physics.Manager;
using OpenMetaverse;
using BulletDotNET;

namespace OpenSim.Region.Physics.BulletDotNETPlugin
{
    public class BulletDotNETScene : PhysicsScene
    {
        // private string m_sceneIdentifier = string.Empty;

        private HashSet<BulletDotNETCharacter> m_characters = new HashSet<BulletDotNETCharacter>();
        private Dictionary<uint, BulletDotNETCharacter> m_charactersLocalID = new Dictionary<uint, BulletDotNETCharacter>();
        private HashSet<BulletDotNETPrim> m_prims = new HashSet<BulletDotNETPrim>();
        private Dictionary<uint, BulletDotNETPrim> m_primsLocalID = new Dictionary<uint, BulletDotNETPrim>();
        private HashSet<BulletDotNETPrim> m_activePrims = new HashSet<BulletDotNETPrim>();
        private HashSet<PhysicsActor> m_taintedActors = new HashSet<PhysicsActor>();
        private HashSet<PhysicsActor> m_waitingtaintedActors = new HashSet<PhysicsActor>();
        private btDiscreteDynamicsWorld m_world;
        private btAxisSweep3 m_broadphase;
        private btCollisionConfiguration m_collisionConfiguration;
        private btConstraintSolver m_solver;
        private btCollisionDispatcher m_dispatcher;
        private btHeightfieldTerrainShape m_terrainShape;
        public btRigidBody TerrainBody;
        private btVector3 m_terrainPosition;
        private btVector3 m_gravity;
        public btMotionState m_terrainMotionState;
        public btTransform m_terrainTransform;
        public btVector3 VectorZero;
        public btQuaternion QuatIdentity;
        public btTransform TransZero;
        public RegionInfo m_region;

        public float geomDefaultDensity = 10.000006836f;

        private float avPIDD = 65f;
        private float avPIDP = 21f;
        private float avCapRadius = 0.37f;
        private float avDensity = 80f;
        private float avHeightFudgeFactor = 0.52f;
        private float avMovementDivisorWalk = 1.8f;
        private float avMovementDivisorRun = 0.8f;

        // private float minimumGroundFlightOffset = 3f;

        public bool meshSculptedPrim = true;

        public float meshSculptLOD = 32;
        public float MeshSculptphysicalLOD = 16;

        public float bodyPIDD = 35f;
        public float bodyPIDG = 25;
        internal int geomCrossingFailuresBeforeOutofbounds = 4;

        public float bodyMotorJointMaxforceTensor = 2;

        public int bodyFramesAutoDisable = 20;

        public float WorldTimeStep = 10f/60f;
        public const float WorldTimeComp = 1 / 60f;
        public float gravityx = 0;
        public float gravityy = 0;
        public float gravityz = -9.8f;
        public float maximumMassObject = 10000.01f;
        public float minimumGroundFlightOffset = 3;

        private short[] _origheightmap;    // Used for Fly height. Kitto Flora
        private bool usingGImpactAlgorithm = false;

        // private IConfigSource m_config;
        private readonly btVector3 worldAabbMin = new btVector3(-10f, -10f, 0);
        private btVector3 worldAabbMax;

        public IMesher mesher;
        private ContactAddedCallbackHandler m_CollisionInterface;

        private bool Locked = false;
        public object BulletLock = null;
        public int geomUpdatesPerThrottledUpdate = 15;
        private List<PhysicsActor> RemoveQueue = new List<PhysicsActor>();
        private bool forceSimplePrimMeshing = false;

        public override bool DisableCollisions
        {
            get { return false; }
            set { }
        }

        public override bool UseUnderWaterPhysics
        {
            get { return false; }
        }

        public BulletDotNETScene(string sceneIdentifier)
        {
            BulletLock = new object();
            // m_sceneIdentifier = sceneIdentifier;
            VectorZero = new btVector3(0, 0, 0);
            QuatIdentity = new btQuaternion(0, 0, 0, 1);
            TransZero = new btTransform(QuatIdentity, VectorZero);
            m_gravity = new btVector3(0, 0, gravityz);
        }

        public override void Initialise(IMesher meshmerizer, RegionInfo region, IRegistryCore registry)
        {
            mesher = meshmerizer;
            m_region = region;
        }

        public override void PostInitialise(IConfigSource config)
        {
            //m_config = config;
            if (config != null)
            {
                IConfig physicsconfig = config.Configs["BulletPhysicsSettings"];
                if (physicsconfig != null)
                {
                    gravityx = physicsconfig.GetFloat("world_gravityx", 0f);
                    gravityy = physicsconfig.GetFloat("world_gravityy", 0f);
                    gravityz = physicsconfig.GetFloat("world_gravityz", -9.8f);

                    avDensity = physicsconfig.GetFloat("av_density", 80f);
                    avHeightFudgeFactor = physicsconfig.GetFloat("av_height_fudge_factor", 0.52f);
                    avMovementDivisorWalk = physicsconfig.GetFloat("av_movement_divisor_walk", 1.3f);
                    avMovementDivisorRun = physicsconfig.GetFloat("av_movement_divisor_run", 0.8f);
                    avCapRadius = physicsconfig.GetFloat("av_capsule_radius", 0.37f);

                    //contactsPerCollision = physicsconfig.GetInt("contacts_per_collision", 80);

                    geomCrossingFailuresBeforeOutofbounds = physicsconfig.GetInt("geom_crossing_failures_before_outofbounds", 4);

                    geomDefaultDensity = physicsconfig.GetFloat("geometry_default_density", 10.000006836f);
                    bodyFramesAutoDisable = physicsconfig.GetInt("body_frames_auto_disable", 20);

                    bodyPIDD = physicsconfig.GetFloat("body_pid_derivative", 35f);
                    bodyPIDG = physicsconfig.GetFloat("body_pid_gain", 25f);

                    meshSculptedPrim = physicsconfig.GetBoolean("mesh_sculpted_prim", true);
                    meshSculptLOD = physicsconfig.GetFloat("mesh_lod", 32f);
                    MeshSculptphysicalLOD = physicsconfig.GetFloat("mesh_physical_lod", 16f);

                    if (Environment.OSVersion.Platform == PlatformID.Unix)
                    {
                        avPIDD = physicsconfig.GetFloat("av_pid_derivative_linux", 65f);
                        avPIDP = physicsconfig.GetFloat("av_pid_proportional_linux", 25);
                        bodyMotorJointMaxforceTensor = physicsconfig.GetFloat("body_motor_joint_maxforce_tensor_linux", 2f);
                    }
                    else
                    {
                        avPIDD = physicsconfig.GetFloat("av_pid_derivative_win", 65f);
                        avPIDP = physicsconfig.GetFloat("av_pid_proportional_win", 25);
                        bodyMotorJointMaxforceTensor = physicsconfig.GetFloat("body_motor_joint_maxforce_tensor_win", 2f);
                    }

                    forceSimplePrimMeshing = physicsconfig.GetBoolean("force_simple_prim_meshing", forceSimplePrimMeshing);
                    minimumGroundFlightOffset = physicsconfig.GetFloat("minimum_ground_flight_offset", 3f);
                    maximumMassObject = physicsconfig.GetFloat("maximum_mass_object", 10000.01f);
                }
            }
            lock (BulletLock)
            {
                worldAabbMax = new btVector3(m_region.RegionSizeX + 10f, m_region.RegionSizeY + 10f, m_region.RegionSizeZ);
                m_broadphase = new btAxisSweep3(worldAabbMin, worldAabbMax, 16000);
                m_collisionConfiguration = new btDefaultCollisionConfiguration();
                m_solver = new btSequentialImpulseConstraintSolver();
                m_dispatcher = new btCollisionDispatcher(m_collisionConfiguration);
                m_world = new btDiscreteDynamicsWorld(m_dispatcher, m_broadphase, m_solver, m_collisionConfiguration);
                m_world.setGravity(m_gravity);
                EnableCollisionInterface();
            }
        }

        public void RemoveAvatarFromList(BulletDotNETCharacter chr)
        {
            lock (m_characters)
            {
                m_charactersLocalID.Remove(chr.m_localID);
                m_characters.Remove(chr);
            }
        }

        public void RemoveCollisionObject(btRigidBody body)
        {
            m_world.removeCollisionObject(body);
        }

        public override PhysicsCharacter AddAvatar (string avName, Vector3 position, Quaternion rotation, Vector3 size, bool isFlying, uint LocalID, UUID UUID)
        {
            lock (BulletLock)
            {
                BulletDotNETCharacter chr = new BulletDotNETCharacter(avName, this, position, size, avPIDD, avPIDP,
                                                                      avCapRadius, avDensity,
                                                                      avHeightFudgeFactor, avMovementDivisorWalk,
                                                                      avMovementDivisorRun);
                chr.LocalID = LocalID;
                try
                {
                    lock (m_characters)
                    {
                        m_characters.Add(chr);
                        m_charactersLocalID.Add(chr.m_localID, chr);
                    }
                }
                catch
                {
                    // noop if it's already there
                    m_log.Debug("[PHYSICS] BulletDotNet: adding duplicate avatar localID");
                }
                AddPhysicsActorTaint(chr);
                return chr;
            }
        }

        public override void RemoveAvatar(PhysicsCharacter actor)
        {
            lock (BulletLock)
            {
                BulletDotNETCharacter chr = (BulletDotNETCharacter)actor;

                if (!Locked)
                {
                    chr.Remove();
                    AddPhysicsActorTaint(chr);
                    //chr = null;
                }
                else
                {
                    RemoveQueue.Add(actor);
                }
                m_charactersLocalID.Remove(chr.m_localID);
            }
        }

        public override void RemovePrim(PhysicsObject prim)
        {
            if (prim is BulletDotNETPrim)
            {
                if (!Locked)
                {
                    lock (BulletLock)
                    {
                        BulletDotNETPrim p = (BulletDotNETPrim)prim;

                        p.setPrimForRemoval();
                        AddPhysicsActorTaint(prim);
                    }
                }
                else
                {
                    RemoveQueue.Add(prim);
                }
            }
        }

        private PhysicsObject AddPrim(String name, Vector3 position, Vector3 size, Quaternion rotation,
                                    IMesh mesh, PrimitiveBaseShape pbs, bool isphysical)
        {

            Vector3 pos = position;
            //pos.X = position.X;
            //pos.Y = position.Y;
            //pos.Z = position.Z;
            Vector3 siz = Vector3.Zero;
            siz.X = size.X;
            siz.Y = size.Y;
            siz.Z = size.Z;
            Quaternion rot = rotation;

            BulletDotNETPrim newPrim;
            lock (BulletLock)
            {

                newPrim = new BulletDotNETPrim(name, this, pos, siz, rot, mesh, pbs, isphysical);

                //lock (m_prims)
                //    m_prims.Add(newPrim);
            }
            

            return newPrim;
        }

        public override PhysicsObject AddPrimShape (ISceneChildEntity entity)
        {
            PhysicsObject result;
            IMesh mesh = null;
            bool isPhysical = ((entity.ParentEntity.RootChild.Flags & PrimFlags.Physics) != 0);
            bool isPhantom = ((entity.ParentEntity.RootChild.Flags & PrimFlags.Phantom) != 0);
            bool physical = isPhysical & !isPhantom;

            if (needsMeshing(entity.Shape))
                mesh = mesher.CreateMesh (entity.Name, entity.Shape, entity.Scale, 32f, physical);

            result = AddPrim (entity.Name, entity.AbsolutePosition, entity.Scale, entity.RotationOffset, mesh, entity.Shape, physical);

            return result;
        }

        public override void AddPhysicsActorTaint(PhysicsActor prim)
        {
            if (!Locked)
            {
                lock (m_taintedActors)
                {
                    if (!m_taintedActors.Contains(prim))
                    {
                        m_taintedActors.Add(prim);
                    }
                }
            }
            else
            {
                m_waitingtaintedActors.Add(prim);
            }
        }
        internal void SetUsingGImpact()
        {
            if (!usingGImpactAlgorithm)
                btGImpactCollisionAlgorithm.registerAlgorithm(m_dispatcher);
                usingGImpactAlgorithm = true;
        }

        public override float Simulate(float timeStep)
        {
            Locked = true;
            float steps = 0;
            lock (BulletLock)
            {
                lock (m_taintedActors)
                {
                    foreach (PhysicsActor act in m_taintedActors)
                    {
                        if (act is BulletDotNETCharacter)
                            ((BulletDotNETCharacter)act).ProcessTaints(timeStep);
                        if (act is BulletDotNETPrim)
                            ((BulletDotNETPrim)act).ProcessTaints(timeStep);
                    }
                    m_taintedActors.Clear();
                }

                lock (m_characters)
                {
                    foreach (BulletDotNETCharacter chr in m_characters)
                    {
                        chr.Move(timeStep);
                    }
                }

                lock (m_prims)
                {
                    foreach (BulletDotNETPrim prim in m_prims)
                    {
                        if (prim != null)
                            prim.Move(timeStep);
                    }
                }
                lock (m_world)
                {
                    steps = m_world.stepSimulation(timeStep, 10, WorldTimeComp);
                }

                foreach (BulletDotNETCharacter chr in m_characters)
                {
                    chr.UpdatePositionAndVelocity();
                }

                foreach (BulletDotNETPrim prm in m_activePrims)
                {
                    /*
                    if (prm != null)
                        if (prm.Body != null)
                    */
                    prm.UpdatePositionAndVelocity();
                }
                /*if (m_CollisionInterface != null)
                {
                    List<BulletDotNETPrim> primsWithCollisions = new List<BulletDotNETPrim>();
                    List<BulletDotNETCharacter> charactersWithCollisions = new List<BulletDotNETCharacter>();

                    // get the collisions that happened this tick
                    List<BulletDotNET.ContactAddedCallbackHandler.ContactInfo> collisions = m_CollisionInterface.GetContactList();
                    // passed back the localID of the prim so we can associate the prim
                    foreach (BulletDotNET.ContactAddedCallbackHandler.ContactInfo ci in collisions)
                    {
                        // ContactPoint = { contactPoint, contactNormal, penetrationDepth }
                        ContactPoint contact = new ContactPoint(new Vector3(ci.pX, ci.pY, ci.pZ),
                                        new Vector3(ci.nX, ci.nY, ci.nZ), ci.depth);

                        ProcessContact(ci.contact, ci.contactWith, contact, ref primsWithCollisions, ref charactersWithCollisions);
                        ProcessContact(ci.contactWith, ci.contact, contact, ref primsWithCollisions, ref charactersWithCollisions);

                    }
                    m_CollisionInterface.Clear();
                    // for those prims and characters that had collisions cause collision events
                    foreach (BulletDotNETPrim bdnp in primsWithCollisions)
                    {
                        bdnp.SendCollisions();
                    }
                    foreach (BulletDotNETCharacter bdnc in charactersWithCollisions)
                    {
                        bdnc.SendCollisions();
                    }
                }*/
            }
            Locked = false;
            //No lock, as the lock that was adding to this was just removed
            if (RemoveQueue.Count > 0)
            {
                do
                {
                    if (RemoveQueue[0] != null)
                    {
                        if (RemoveQueue[0] is BulletDotNETPrim)
                        {
                            BulletDotNETPrim prim = RemoveQueue[0] as BulletDotNETPrim;
                            prim.Dispose();
                        }
                        else if (RemoveQueue[0] is BulletDotNETCharacter)
                        {
                            BulletDotNETCharacter chr = RemoveQueue[0] as BulletDotNETCharacter;
                            chr.Dispose();
                        }
                    }
                    RemoveQueue.RemoveAt(0);
                }
                while (RemoveQueue.Count > 0);
            }
            //No lock, as the lock that was adding to this was just removed
            if (m_waitingtaintedActors.Count != 0)
            {
                foreach (PhysicsActor actor in m_waitingtaintedActors)
                {
                    if (!m_taintedActors.Contains(actor))
                        m_taintedActors.Add(actor);
                }
            }
            return steps;
        }

        private void ProcessContact(uint cont, uint contWith, ContactPoint contact, 
                    ref List<BulletDotNETPrim> primsWithCollisions,
                    ref List<BulletDotNETCharacter> charactersWithCollisions)
        {
            BulletDotNETPrim bdnp;
            // collisions with a normal prim?
            if (m_primsLocalID.TryGetValue(cont, out bdnp))
            {
                // Added collision event to the prim. This creates a pile of events
                // that will be sent to any subscribed listeners.
                bdnp.AddCollisionEvent (contWith, contact);
                if (!primsWithCollisions.Contains(bdnp))
                {
                    primsWithCollisions.Add(bdnp);
                }
            }
            else
            {
                BulletDotNETCharacter bdnc;
                // if not a prim, maybe it's one of the characters
                if (m_charactersLocalID.TryGetValue(cont, out bdnc))
                {
                    bdnc.AddCollisionEvent(contWith, contact);
                    if (!charactersWithCollisions.Contains(bdnc))
                    {
                        charactersWithCollisions.Add(bdnc);
                    }
                }
            }
        }

        public override void SetTerrain (ITerrainChannel channel, short[] shortheightMap)
        {
            if (m_terrainShape != null)
                DeleteTerrain();

            float hfmax = 256;
            float hfmin = 0;
            
            // store this for later reference.
            // Note, we're storing it  after we check it for anomolies above
            _origheightmap = shortheightMap;

            hfmin = 0;
            hfmax = 256;

            float[] heightmap = new float[m_region.RegionSizeX * m_region.RegionSizeX];
            for (int i = 0; i < shortheightMap.Length; i++)
            {
                heightmap[i] = shortheightMap[i] / Constants.TerrainCompression;
            }

            m_terrainShape = new btHeightfieldTerrainShape(m_region.RegionSizeX, m_region.RegionSizeY, heightmap,
                                                           1.0f, hfmin, hfmax, (int)btHeightfieldTerrainShape.UPAxis.Z,
                                                           (int)btHeightfieldTerrainShape.PHY_ScalarType.PHY_FLOAT, false);
            float AabbCenterX = m_region.RegionSizeX / 2f;
            float AabbCenterY = m_region.RegionSizeY / 2f;

            float AabbCenterZ = 0;
            float temphfmin, temphfmax;

            temphfmin = hfmin;
            temphfmax = hfmax;

            if (temphfmin < 0)
            {
                temphfmax = 0 - temphfmin;
                temphfmin = 0 - temphfmin;
            }
            else if (temphfmin > 0)
            {
                temphfmax = temphfmax + (0 - temphfmin);
                //temphfmin = temphfmin + (0 - temphfmin);
            }
            AabbCenterZ = temphfmax/2f;
            
            if (m_terrainPosition == null)
            {
                m_terrainPosition = new btVector3(AabbCenterX, AabbCenterY, AabbCenterZ);
            }
            else
            {
                try
                {
                    m_terrainPosition.setValue(AabbCenterX, AabbCenterY, AabbCenterZ);
                } 
                catch (ObjectDisposedException)
                {
                    m_terrainPosition = new btVector3(AabbCenterX, AabbCenterY, AabbCenterZ);
                }
            }
            if (m_terrainMotionState != null)
            {
                m_terrainMotionState.Dispose();
                m_terrainMotionState = null;
            }
            m_terrainTransform = new btTransform(QuatIdentity, m_terrainPosition);
            m_terrainMotionState = new btDefaultMotionState(m_terrainTransform);
            TerrainBody = new btRigidBody(0, m_terrainMotionState, m_terrainShape);
            TerrainBody.setUserPointer((IntPtr)0);
            m_world.addRigidBody(TerrainBody);
        }

        public override void SetWaterLevel(double height, short[] map)
        {
            
        }

        public void DeleteTerrain()
        {
            if (TerrainBody != null)
            {
                m_world.removeRigidBody(TerrainBody);
            }

            if (m_terrainShape != null)
            {
                m_terrainShape.Dispose();
                m_terrainShape = null;
            }

            if (m_terrainMotionState != null)
            {
                m_terrainMotionState.Dispose();
                m_terrainMotionState = null;
            }
            
            if (m_terrainTransform != null)
            {
                m_terrainTransform.Dispose();
                m_terrainTransform = null;
            }

            if (m_terrainPosition != null)
            {
                m_terrainPosition.Dispose();
                m_terrainPosition = null;
            }
        }

        public override void Dispose()
        {
            lock (BulletLock)
            {
                disposeAllBodies();
                m_world.Dispose();
                m_broadphase.Dispose();
                ((btDefaultCollisionConfiguration)m_collisionConfiguration).Dispose();
                ((btSequentialImpulseConstraintSolver)m_solver).Dispose();
                worldAabbMax.Dispose();
                worldAabbMin.Dispose();
                VectorZero.Dispose();
                QuatIdentity.Dispose();
                m_gravity.Dispose();
                VectorZero = null;
                QuatIdentity = null;
            }
        }

        public override Dictionary<uint, float> GetTopColliders()
        {
            Dictionary<uint, float> returncolliders = new Dictionary<uint, float>();
            int cnt = 0;
            lock (m_prims)
            {
                foreach (BulletDotNETPrim prm in m_prims)
                {
                    if (prm.CollisionScore > 0)
                    {
                        returncolliders.Add(prm.m_localID, prm.CollisionScore);
                        cnt++;
                        prm.CollisionScore = 0f;
                        if (cnt > 25)
                        {
                            break;
                        }
                    }
                }
            }
            return returncolliders;
        }

        public btDiscreteDynamicsWorld getBulletWorld()
        {
            return m_world;
        }

        private void disposeAllBodies()
        {
            lock (m_prims)
            {
                m_primsLocalID.Clear();
                foreach (BulletDotNETPrim prim in m_prims)
                {
                    if (prim.Body != null)
                        m_world.removeRigidBody(prim.Body);

                    prim.Dispose();
                }
                m_prims.Clear();

                foreach (BulletDotNETCharacter chr in m_characters)
                {
                    if (chr.Body != null)
                        m_world.removeRigidBody(chr.Body);
                    chr.Dispose();
                }
                m_characters.Clear();
            }
        }

        public override bool IsThreaded
        {
            get { return false; }
        }

        internal void addCollisionEventReporting(PhysicsActor bulletDotNETCharacter)
        {
            //TODO: FIXME:
        }

        internal void remCollisionEventReporting(PhysicsActor bulletDotNETCharacter)
        {
            //TODO: FIXME:
        }

        internal void AddRigidBody(btRigidBody Body)
        {
            m_world.addRigidBody(Body);
        }
        
        internal void removeFromWorld(BulletDotNETCharacter chr, btRigidBody body)
        {
            lock (m_characters)
            {
                if (m_characters.Contains(chr))
                {
                    m_world.removeRigidBody(body);
                    m_characters.Remove(chr);
                }
            }
        }

        internal void removeFromWorld(BulletDotNETPrim prm ,btRigidBody body)
        {
            lock (m_prims)
            {
                if (m_prims.Contains(prm))
                {
                    m_world.removeRigidBody(body);
                }
                remActivePrim(prm);
                m_primsLocalID.Remove(prm.m_localID);
                m_prims.Remove(prm);
            }

        }

        internal float GetWaterLevel()
        {
            return 0;
        }

        // Recovered for use by fly height. Kitto Flora
        public float GetTerrainHeightAtXY(float x, float y)
        {
            // Teravus: Kitto, this code causes recurring errors that stall physics permenantly unless 
            // the values are checked, so checking below.
            // Is there any reason that we don't do this in ScenePresence?
            // The only physics engine that benefits from it in the physics plugin is this one

            if (x > m_region.RegionSizeX || y > m_region.RegionSizeY ||
                x < 0.001f || y < 0.001f)
                return 0;

            return _origheightmap[(int)y * m_region.RegionSizeY + (int)x] / Constants.TerrainCompression;
        }
        // End recovered. Kitto Flora

        /// <summary>
        /// Routine to figure out if we need to mesh this prim with our mesher
        /// </summary>
        /// <param name="pbs"></param>
        /// <returns></returns>
        public bool needsMeshing(PrimitiveBaseShape pbs)
        {
            if (forceSimplePrimMeshing)
                return true;
            // most of this is redundant now as the mesher will return null if it cant mesh a prim
            // but we still need to check for sculptie meshing being enabled so this is the most
            // convenient place to do it for now...

            //    //if (pbs.PathCurve == (byte)Primitive.PathCurve.Circle && pbs.ProfileCurve == (byte)Primitive.ProfileCurve.Circle && pbs.PathScaleY <= 0.75f)
            //    //m_log.Debug("needsMeshing: " + " pathCurve: " + pbs.PathCurve.ToString() + " profileCurve: " + pbs.ProfileCurve.ToString() + " pathScaleY: " + Primitive.UnpackPathScale(pbs.PathScaleY).ToString());
            int iPropertiesNotSupportedDefault = 0;

            if (pbs.SculptEntry && !meshSculptedPrim)
            {
#if SPAM
                m_log.Warn("NonMesh");
#endif
                return false;
            }

            // if it's a standard box or sphere with no cuts, hollows, twist or top shear, return false since ODE can use an internal representation for the prim
            if ((pbs.ProfileShape == ProfileShape.Square && pbs.PathCurve == (byte)Extrusion.Straight)
                || (pbs.ProfileShape == ProfileShape.HalfCircle && pbs.PathCurve == (byte)Extrusion.Curve1
                && pbs.Scale.X == pbs.Scale.Y && pbs.Scale.Y == pbs.Scale.Z))
            {

                if (pbs.ProfileBegin == 0 && pbs.ProfileEnd == 0
                    && pbs.ProfileHollow == 0
                    && pbs.PathTwist == 0 && pbs.PathTwistBegin == 0
                    && pbs.PathBegin == 0 && pbs.PathEnd == 0
                    && pbs.PathTaperX == 0 && pbs.PathTaperY == 0
                    && pbs.PathScaleX == 100 && pbs.PathScaleY == 100
                    && pbs.PathShearX == 0 && pbs.PathShearY == 0)
                {
#if SPAM
                    m_log.Warn("NonMesh");
#endif
                    return false;
                }
            }

            if (pbs.ProfileHollow != 0)
                iPropertiesNotSupportedDefault++;

            if ((pbs.PathTwistBegin != 0) || (pbs.PathTwist != 0))
                iPropertiesNotSupportedDefault++;

            if ((pbs.ProfileBegin != 0) || pbs.ProfileEnd != 0)
                iPropertiesNotSupportedDefault++;

            if ((pbs.PathScaleX != 100) || (pbs.PathScaleY != 100))
                iPropertiesNotSupportedDefault++;

            if ((pbs.PathShearX != 0) || (pbs.PathShearY != 0))
                iPropertiesNotSupportedDefault++;

            if (pbs.ProfileShape == ProfileShape.Circle && pbs.PathCurve == (byte)Extrusion.Straight)
                iPropertiesNotSupportedDefault++;

            if (pbs.ProfileShape == ProfileShape.HalfCircle && pbs.PathCurve == (byte)Extrusion.Curve1 && (pbs.Scale.X != pbs.Scale.Y || pbs.Scale.Y != pbs.Scale.Z || pbs.Scale.Z != pbs.Scale.X))
                iPropertiesNotSupportedDefault++;

            if (pbs.ProfileShape == ProfileShape.HalfCircle && pbs.PathCurve == (byte)Extrusion.Curve1)
                iPropertiesNotSupportedDefault++;

            // test for torus
            if ((pbs.ProfileCurve & 0x07) == (byte)ProfileShape.Square)
            {
                if (pbs.PathCurve == (byte)Extrusion.Curve1)
                {
                    iPropertiesNotSupportedDefault++;
                }
            }
            else if ((pbs.ProfileCurve & 0x07) == (byte)ProfileShape.Circle)
            {
                if (pbs.PathCurve == (byte)Extrusion.Straight)
                {
                    iPropertiesNotSupportedDefault++;
                }

                // ProfileCurve seems to combine hole shape and profile curve so we need to only compare against the lower 3 bits
                else if (pbs.PathCurve == (byte)Extrusion.Curve1)
                {
                    iPropertiesNotSupportedDefault++;
                }
            }
            else if ((pbs.ProfileCurve & 0x07) == (byte)ProfileShape.HalfCircle)
            {
                if (pbs.PathCurve == (byte)Extrusion.Curve1 || pbs.PathCurve == (byte)Extrusion.Curve2)
                {
                    iPropertiesNotSupportedDefault++;
                }
            }
            else if ((pbs.ProfileCurve & 0x07) == (byte)ProfileShape.EquilateralTriangle)
            {
                if (pbs.PathCurve == (byte)Extrusion.Straight)
                {
                    iPropertiesNotSupportedDefault++;
                }
                else if (pbs.PathCurve == (byte)Extrusion.Curve1)
                {
                    iPropertiesNotSupportedDefault++;
                }
            }


            if (iPropertiesNotSupportedDefault == 0)
            {
#if SPAM
                m_log.Warn("NonMesh");
#endif
                return false;
            }
#if SPAM
            m_log.Debug("Mesh");
#endif
            return true;
        }

        internal void addActivePrim(BulletDotNETPrim pPrim)
        {
            lock (m_activePrims)
            {
                if (!m_activePrims.Contains(pPrim))
                {
                    m_activePrims.Add(pPrim);
                }
            }
        }

        public void remActivePrim(BulletDotNETPrim pDeactivatePrim)
        {
            lock (m_activePrims)
            {
                m_activePrims.Remove(pDeactivatePrim);
            }
        }

        internal void AddPrimToScene(BulletDotNETPrim pPrim)
        {
            lock (m_prims)
            {
                if (!m_prims.Contains(pPrim))
                {
                    try
                    {
                        m_prims.Add(pPrim);
                        m_primsLocalID.Add(pPrim.m_localID, pPrim);
                    }
                    catch
                    {
                        // noop if it's already there
                        m_log.Debug("[PHYSICS] BulletDotNet: adding duplicate prim localID");
                    }
                    m_world.addRigidBody(pPrim.Body);
                    // m_log.Debug("[PHYSICS] added prim to scene");
                }
            }
        }
        internal void EnableCollisionInterface()
        {
            if (m_CollisionInterface == null)
            {
                m_CollisionInterface = new ContactAddedCallbackHandler(m_world);
                // m_world.SetCollisionAddedCallback(m_CollisionInterface);
            }
        }
    }
}

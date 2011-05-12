/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
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
using System.IO;
using System.Reflection;
using OpenMetaverse;
using log4net;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using System.Collections.Generic;
using System.Xml;

namespace OpenSim.Region.Framework.Scenes
{
    public partial class SceneObjectGroup : EntityBase
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Force all task inventories of prims in the scene object to persist
        /// </summary>
        public void ForceInventoryPersistence()
        {
            Util.GetReaderLock (m_partsLock);
            foreach (SceneObjectPart part in m_partsList)
            {
                part.Inventory.ForceInventoryPersistence ();
            }
            Util.ReleaseReaderLock (m_partsLock);
        }

        public void BackupPreparation()
        {
            Util.GetReaderLock (m_partsLock);
            foreach (SceneObjectPart part in m_partsList)
            {
                part.Inventory.SaveScriptStateSaves ();
            }
            Util.ReleaseReaderLock (m_partsLock);
        }

        /// <summary>
        /// Start the scripts contained in all the prims in this group.
        /// </summary>
        public void CreateScriptInstances(int startParam, bool postOnRez,
                int stateSource, UUID RezzedFrom)
        {
            // Don't start scripts if they're turned off in the region!
            if (!m_scene.RegionInfo.RegionSettings.DisableScripts)
            {
                Util.GetReaderLock (m_partsLock);
                foreach (SceneObjectPart part in m_partsList)
                {
                    part.Inventory.CreateScriptInstances (startParam, postOnRez, stateSource, RezzedFrom);
                }
                Util.ReleaseReaderLock (m_partsLock);
            }
        }

        /// <summary>
        /// Stop the scripts contained in all the prims in this group
        /// </summary>
        /// <param name="sceneObjectBeingDeleted">
        /// Should be true if these scripts are being removed because the scene
        /// object is being deleted.  This will prevent spurious updates to the client.
        /// </param>
        public void RemoveScriptInstances(bool sceneObjectBeingDeleted)
        {
            Util.GetReaderLock (m_partsLock);
            foreach (SceneObjectPart part in m_partsList)
            {
                part.Inventory.RemoveScriptInstances (sceneObjectBeingDeleted);
            }
            Util.ReleaseReaderLock (m_partsLock);
        }

        /// <summary>
        /// Add an inventory item to a prim in this group.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="localID"></param>
        /// <param name="item"></param>
        /// <param name="copyItemID">The item UUID that should be used by the new item.</param>
        /// <returns></returns>
        public bool AddInventoryItem(IClientAPI remoteClient, uint localID,
                                     InventoryItemBase item, UUID copyItemID)
        {
            UUID newItemId = (copyItemID != UUID.Zero) ? copyItemID : item.ID;

            SceneObjectPart part = (SceneObjectPart)GetChildPart(localID);
            if (part != null)
            {
                TaskInventoryItem taskItem = new TaskInventoryItem();

                taskItem.ItemID = newItemId;
                taskItem.AssetID = item.AssetID;
                taskItem.Name = item.Name;
                taskItem.Description = item.Description;
                taskItem.OwnerID = part.OwnerID; // Transfer ownership
                taskItem.CreatorID = item.CreatorIdAsUuid;
                taskItem.Type = item.AssetType;
                taskItem.InvType = item.InvType;

                if (remoteClient != null &&
                        remoteClient.AgentId != part.OwnerID &&
                        m_scene.Permissions.PropagatePermissions())
                {
                    taskItem.BasePermissions = item.BasePermissions &
                            item.NextPermissions;
                    taskItem.CurrentPermissions = item.CurrentPermissions &
                            item.NextPermissions;
                    taskItem.EveryonePermissions = item.EveryOnePermissions &
                            item.NextPermissions;
                    taskItem.GroupPermissions = item.GroupPermissions &
                            item.NextPermissions;
                    taskItem.NextPermissions = item.NextPermissions;
                    // We're adding this to a prim we don't own. Force
                    // owner change
                    taskItem.CurrentPermissions |= 16; // Slam
                } 
                else 
                {
                    taskItem.BasePermissions = item.BasePermissions;
                    taskItem.CurrentPermissions = item.CurrentPermissions;
                    taskItem.EveryonePermissions = item.EveryOnePermissions;
                    taskItem.GroupPermissions = item.GroupPermissions;
                    taskItem.NextPermissions = item.NextPermissions;
                }

                taskItem.Flags = item.Flags;
                taskItem.SalePrice = item.SalePrice;
                taskItem.SaleType = item.SaleType;
                taskItem.CreationDate = (uint)item.CreationDate;

                bool addFromAllowedDrop = false;
                if (remoteClient!=null) 
                {
                    addFromAllowedDrop = remoteClient.AgentId != part.OwnerID;
                }

                part.Inventory.AddInventoryItem(taskItem, addFromAllowedDrop);

                return true;
            }
            else
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: " +
                    "Couldn't find prim local ID {0} in group {1}, {2} to add inventory item ID {3}",
                    localID, Name, UUID, newItemId);
            }

            return false;
        }

        /// <summary>
        /// Returns an existing inventory item.  Returns the original, so any changes will be live.
        /// </summary>
        /// <param name="primID"></param>
        /// <param name="itemID"></param>
        /// <returns>null if the item does not exist</returns>
        public TaskInventoryItem GetInventoryItem(uint primID, UUID itemID)
        {
            SceneObjectPart part = (SceneObjectPart)GetChildPart (primID);
            if (part != null)
            {
                return part.Inventory.GetInventoryItem(itemID);
            }
            else
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: " +
                    "Couldn't find prim local ID {0} in prim {1}, {2} to get inventory item ID {3}",
                    primID, part.Name, part.UUID, itemID);
            }

            return null;
        }

        /// <summary>
        /// Update an existing inventory item.
        /// </summary>
        /// <param name="item">The updated item.  An item with the same id must already exist
        /// in this prim's inventory</param>
        /// <returns>false if the item did not exist, true if the update occurred succesfully</returns>
        public bool UpdateInventoryItem(TaskInventoryItem item)
        {
            SceneObjectPart part = (SceneObjectPart)GetChildPart (item.ParentPartID);
            if (part != null)
            {
                part.Inventory.UpdateInventoryItem(item);

                return true;
            }
            else
            {
                m_log.ErrorFormat(
                    "[PRIM INVENTORY]: " +
                    "Couldn't find prim ID {0} to update item {1}, {2}",
                    item.ParentPartID, item.Name, item.ItemID);
            }

            return false;
        }

        public int RemoveInventoryItem(uint localID, UUID itemID)
        {
            SceneObjectPart part = (SceneObjectPart)GetChildPart (localID);
            if (part != null)
            {
                int type = part.Inventory.RemoveInventoryItem(itemID);

                return type;
            }

            return -1;
        }

        public uint GetEffectivePermissions()
        {
            uint perms=(uint)(PermissionMask.Modify |
                              PermissionMask.Copy |
                              PermissionMask.Move |
                              PermissionMask.Transfer) | 7;

            uint ownerMask = 0x7ffffff;
            Util.GetReaderLock (m_partsLock);
            foreach (SceneObjectPart part in m_partsList)
            {
                ownerMask &= part.OwnerMask;
                perms &= part.Inventory.MaskEffectivePermissions ();
            }
            Util.ReleaseReaderLock (m_partsLock);

            if ((ownerMask & (uint)PermissionMask.Modify) == 0)
                perms &= ~(uint)PermissionMask.Modify;
            if ((ownerMask & (uint)PermissionMask.Copy) == 0)
                perms &= ~(uint)PermissionMask.Copy;
            if ((ownerMask & (uint)PermissionMask.Transfer) == 0)
                perms &= ~(uint)PermissionMask.Transfer;

            // If root prim permissions are applied here, this would screw
            // with in-inventory manipulation of the next owner perms
            // in a major way. So, let's move this to the give itself.
            // Yes. I know. Evil.
//            if ((ownerMask & RootPart.NextOwnerMask & (uint)PermissionMask.Modify) == 0)
//                perms &= ~((uint)PermissionMask.Modify >> 13);
//            if ((ownerMask & RootPart.NextOwnerMask & (uint)PermissionMask.Copy) == 0)
//                perms &= ~((uint)PermissionMask.Copy >> 13);
//            if ((ownerMask & RootPart.NextOwnerMask & (uint)PermissionMask.Transfer) == 0)
//                perms &= ~((uint)PermissionMask.Transfer >> 13);

            return perms;
        }

        public void ApplyNextOwnerPermissions()
        {
            Util.GetReaderLock (m_partsLock);
            foreach (SceneObjectPart part in m_partsList)
            {
                part.ApplyNextOwnerPermissions ();
            }
            Util.ReleaseReaderLock (m_partsLock);
        }

        public void ResumeScripts()
        {
            Util.GetReaderLock (m_partsLock);
            foreach (SceneObjectPart part in m_partsList)
            {
                part.Inventory.ResumeScripts ();
            }
            Util.ReleaseReaderLock (m_partsLock);
        }
    }
}

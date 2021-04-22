﻿using RagnarsRokare.MobAI;
using Stateless;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlaveGreylings
{
    class SearchForItemsBehaviour : IBehaviour
    {
        private const string Prefix = "RR_SFI";

        private const string Main_state = Prefix + "Main";
        private const string SearchItemsOnGround_state = Prefix + "SearchItemsOnGround";
        private const string MoveToGroundItem_state = Prefix + "MoveToGroundItem"; 
        private const string SearchForRandomContainer_state = Prefix + "SearchForRandomContainer";
        private const string MoveToContainer_state = Prefix + "MoveToContainer";
        private const string OpenContainer_state = Prefix + "OpenContainer";
        private const string SearchForItem_state = Prefix + "SearchForItem"; 
        private const string PickUpItemFromGround_state = Prefix + "PickUpItemFromGround";


        private const string ItemFound_trigger = Prefix + "ItemFound";
        private const string ItemNotFound_trigger = Prefix + "ItemNotFound";
        private const string ContainerFound_trigger = Prefix + "ContainerFound";
        private const string ContainerNotFound_trigger = Prefix + "ContainerNotFound";
        private const string ContainerIsClose_trigger = Prefix + "ContainerIsClose";
        private const string Failed_trigger = Prefix + "Failed";
        private const string ContainerOpened_trigger = Prefix + "ContainerOpened";
        private const string Timeout_trigger = Prefix + "Timeout";
        private const string Update_trigger = Prefix + "Update";
        private const string GroundItemIsClose_trigger = Prefix + "GroundItemIsClose";
        private const string FoundGroundItem_Trigger = Prefix + "FoundGroundItem";

        StateMachine<string, string>.TriggerWithParameters<(MobAIBase aiBase, float dt)> UpdateTrigger;
        StateMachine<string, string>.TriggerWithParameters<ItemDrop> FoundGroundItemTrigger;
        private float OpenChestTimer;
        private float CurrentSearchTime;

        public IEnumerable<ItemDrop.ItemData> Items { get; set; }
        public MaxStack<Container> KnownContainers { get; set; }
        public string[] AcceptedContainerNames { get; set; }

        public ItemDrop.ItemData FoundItem { get; private set; }
        public float OpenChestDelay { get; private set; } = 1;

        public float MaxSearchTime { get; set; } = 60;

        public string InitState { get { return Main_state; } }

        private ItemDrop GroundItem;
        private MobAIBase m_aiBase;


        public void Configure(StateMachine<string, string> brain, string SuccessState, string FailState, string parentState)
        {
            UpdateTrigger = brain.SetTriggerParameters<(MobAIBase aiBase, float dt)>(Update_trigger);
            FoundGroundItemTrigger = brain.SetTriggerParameters<ItemDrop>(FoundGroundItem_Trigger);

            brain.Configure(Main_state)
                .SubstateOf(parentState)
                .Permit(UpdateTrigger.Trigger, SearchItemsOnGround_state)
                .Permit(Timeout_trigger, FailState)
                .OnEntry(t =>
                {
                    Debug.Log("Entered SearchForItemsBehaviour");
                });

            brain.Configure(SearchItemsOnGround_state)
                .SubstateOf(Main_state)
                .Permit(FoundGroundItemTrigger.Trigger, MoveToGroundItem_state)
                .Permit(Failed_trigger, SearchForRandomContainer_state)
                .OnEntry(t =>
                {
                    var args = ((MobAIBase aiBase, float dt))t.Parameters[0];
                    m_aiBase = args.aiBase;
                    ItemDrop groundItem = Common.GetNearbyItem(m_aiBase.Instance.transform.position, Items, GreylingsConfig.ItemSearchRadius.Value);
                    if (groundItem != null)
                    {
                        Debug.Log($"GroundItem:{groundItem.m_itemData.m_dropPrefab.name ?? string.Empty}");
                        brain.Fire(FoundGroundItemTrigger, groundItem);
                    }
                    brain.Fire(Failed_trigger);
                });

            brain.Configure(SearchForRandomContainer_state)
                .SubstateOf(Main_state)
                .Permit(ContainerFound_trigger, MoveToContainer_state)
                .Permit(ContainerNotFound_trigger, FailState)
                .Permit(Failed_trigger, FailState)
                .OnEntry(t =>
                {
                    if (KnownContainers.Any())
                    {
                        var matchingContainer = KnownContainers.Where(c => c.GetInventory().GetAllItems().Any(i => Items.Any(it => i.m_shared.m_name == it.m_shared.m_name))).RandomOrDefault();
                        KnownContainers.Remove(matchingContainer);
                        KnownContainers.Push(matchingContainer);
                        brain.Fire(ContainerFound_trigger);
                    }
                    else
                    {
                        Container nearbyChest = Common.FindRandomNearbyContainer(m_aiBase.Instance.transform.position, KnownContainers, AcceptedContainerNames);
                        if (nearbyChest != null)
                        {
                            KnownContainers.Push(nearbyChest);
                            m_aiBase.Brain.Fire(ContainerFound_trigger);
                        }
                        else
                        {
                            KnownContainers.Clear();
                            m_aiBase.Brain.Fire(ContainerNotFound_trigger);
                        }
                    }
                });

            brain.Configure(MoveToGroundItem_state)
                .SubstateOf(Main_state)
                .Permit(GroundItemIsClose_trigger, PickUpItemFromGround_state)
                .Permit(Failed_trigger, SearchItemsOnGround_state)
                .OnEntry(t =>
                {
                    GroundItem = t.Parameters[0] as ItemDrop;
                });


            brain.Configure(PickUpItemFromGround_state)
                .SubstateOf(Main_state)
                .Permit(ItemFound_trigger, SuccessState)
                .Permit(Failed_trigger, SearchItemsOnGround_state)
                .OnEntry(t =>
                {
                    FoundItem = GroundItem.m_itemData;
                    if (GroundItem.RemoveOne())
                    {
                        brain.Fire(ItemFound_trigger);
                    }
                    brain.Fire(Failed_trigger);
                });


            brain.Configure(MoveToContainer_state)
                .SubstateOf(Main_state)
                .Permit(ContainerIsClose_trigger, OpenContainer_state)
                .Permit(Failed_trigger, SearchForRandomContainer_state)
                .Permit(ContainerNotFound_trigger, FailState)
                .OnEntry(t =>
                {

                });
            brain.Configure(OpenContainer_state)
                .SubstateOf(Main_state)
                .Permit(ContainerOpened_trigger, SearchForItem_state)
                .Permit(Failed_trigger, SearchForRandomContainer_state)
                .Permit(ContainerNotFound_trigger, FailState)
                .OnEntry(t =>
                {
                    if (KnownContainers.Peek().IsInUse())
                    {
                        brain.Fire(Failed_trigger);
                    }
                    KnownContainers.Peek().SetInUse(inUse: true);
                    OpenChestTimer = 0f;
                });

            brain.Configure(SearchForItem_state)
                .SubstateOf(Main_state)
                .Permit(ItemFound_trigger, SuccessState)
                .Permit(ItemNotFound_trigger, SearchForRandomContainer_state)
                .Permit(ContainerNotFound_trigger, FailState)
                .OnEntry(t =>
                {
                    FoundItem = KnownContainers.Peek().GetInventory().GetAllItems().Where(i => Items.Any(it => i.m_shared.m_name == it.m_shared.m_name)).RandomOrDefault();
                    if (FoundItem != null)
                    {
                        KnownContainers.Peek().GetInventory().RemoveItem(FoundItem, 1);
                        Common.Invoke<Container>(KnownContainers.Peek(), "Save");
                        Common.Invoke<Inventory>(KnownContainers.Peek(), "Changed");
                        brain.Fire(ItemFound_trigger);
                    }
                    else
                    {
                        brain.Fire(ItemNotFound_trigger);
                    }
                })
                .OnExit(t =>
                {
                    KnownContainers.Peek().SetInUse(inUse: false);
                });
        }

        public void Update(MobAIBase aiBase, float dt)
        {
            Debug.Log("1");
            if (m_aiBase == null)
            {
                Debug.Log("2");
                aiBase.Brain.Fire(UpdateTrigger, (aiBase, dt));
            }
            if ((CurrentSearchTime += dt) > MaxSearchTime)
            {
                Debug.Log("3");
                aiBase.Brain.Fire(Timeout_trigger);
            }

            Debug.Log("4");
            if (aiBase.Brain.IsInState(MoveToContainer_state))
            {
                Debug.Log("5");
                bool containerIsInvalid = KnownContainers.Peek()?.GetComponent<ZNetView>()?.IsValid() == false;
                if (containerIsInvalid)
                {
                    Debug.Log("6");
                    KnownContainers.Pop();
                    aiBase.Brain.Fire(Failed_trigger);
                    return;
                }
                Common.Invoke<MonsterAI>(aiBase.Instance, "MoveAndAvoid", dt, KnownContainers.Peek().transform.position, 0.5f, false);
                if (Vector3.Distance(aiBase.Instance.transform.position, KnownContainers.Peek().transform.position) < 1.5)
                {
                    Debug.Log("7");
                    aiBase.Brain.Fire(ContainerIsClose_trigger);
                }
                return;
            }

            if (aiBase.Brain.IsInState(MoveToGroundItem_state))
            {
                Debug.Log("8");
                if (GroundItem?.GetComponent<ZNetView>()?.IsValid() != true)
                {
                    Debug.Log("9");
                    GroundItem = null;
                    aiBase.Brain.Fire(Failed_trigger);
                    return;
                }
                Debug.Log("10");
                Common.Invoke<MonsterAI>(aiBase.Instance, "MoveAndAvoid", dt, GroundItem.transform.position, 0.5f, false);
                if (Vector3.Distance(aiBase.Instance.transform.position, GroundItem.transform.position) < 1.5)
                {
                    Debug.Log("11");
                    aiBase.Brain.Fire(GroundItemIsClose_trigger);
                }
                Debug.Log("12");
                return;
            }

            if (aiBase.Brain.IsInState(OpenContainer_state))
            {
                Debug.Log("13");
                if ((OpenChestTimer += dt) > OpenChestDelay)
                {
                    Debug.Log("14");
                    aiBase.Brain.Fire(ContainerOpened_trigger);
                }
            }

        }
    }
}

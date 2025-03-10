using Arrowgene.Ddon.Server;
using Arrowgene.Ddon.Server.Network;
using Arrowgene.Ddon.Shared.Entity.PacketStructure;
using Arrowgene.Ddon.Shared.Entity.Structure;
using Arrowgene.Ddon.Shared.Model;
using Arrowgene.Logging;
using System.Linq;

namespace Arrowgene.Ddon.GameServer.Handler
{
    public class ItemUseBagItemHandler : GameRequestPacketQueueHandler<C2SItemUseBagItemReq, S2CItemUseBagItemRes>
    {
        private static readonly ServerLogger Logger = LogProvider.Logger<ServerLogger>(typeof(ItemUseBagItemHandler));

        private static readonly StorageType DestinationStorageType = StorageType.ItemBagConsumable;
        private DdonGameServer _Server;

        public ItemUseBagItemHandler(DdonGameServer server) : base(server)
        {
            _Server = server;
        }

        public override PacketQueue Handle(GameClient client, C2SItemUseBagItemReq request)
        {
            PacketQueue queue = new();

            S2CItemUseBagItemRes res = new S2CItemUseBagItemRes();
            client.Enqueue(res, queue);

            // TODO: Send S2CItemUseBagItemNtc?

            var tuple = client.Character.Storage.GetStorage(DestinationStorageType).Items
                .Select((x, index) => new { item = x, slot = index + 1 })
                .Where(tuple => tuple.item?.Item1.UId == request.ItemUId)
                .First();
            Item item = tuple.item.Item1;
            uint itemNum = tuple.item.Item2;
            ushort slotNo = (ushort)tuple.slot;

            itemNum--;

            S2CItemUpdateCharacterItemNtc ntc = new S2CItemUpdateCharacterItemNtc()
            {
                UpdateType = ItemNoticeType.UseBag
            };

            if (_Server.ItemManager.IsSecretAbilityItem(item.ItemId))
            {
                _Server.JobManager.UnlockSecretAbility(client, client.Character, (SecretAbility)_Server.ItemManager.GetAbilityId(item.ItemId));
            }

            if (_Server.ScriptManager.GameItemModule.HasItem(item.ItemId))
            {
                _Server.ScriptManager.GameItemModule.GetItemInterface(item.ItemId)?.OnUse(client);
            }

            if (_Server.EpitaphRoadManager.TrialInProgress(client.Party))
            {
                _Server.EpitaphRoadManager.EvaluateItemUsed(client.Party, item.ItemId);
            }

            CDataItemUpdateResult ntcData0 = new CDataItemUpdateResult()
            {
                ItemList = new()
                {
                    ItemUId = item.UId,
                    ItemId = item.ItemId,
                    ItemNum = itemNum,
                    SafetySetting = item.SafetySetting,
                    StorageType = DestinationStorageType,
                    SlotNo = slotNo,
                    Color = item.Color, // ?
                    PlusValue = item.PlusValue, // ?
                    Bind = false,
                    EquipPoint = item.EquipPoints,
                    EquipCharacterID = 0,
                    EquipPawnID = 0,
                    EquipElementParamList = item.EquipElementParamList,
                    AddStatusParamList = item.AddStatusParamList,
                    Unk2List = item.Unk2List
                },
                UpdateItemNum = -(int)request.Amount
            };

            ntc.UpdateItemList.Add(ntcData0);

            if (itemNum == 0)
            {
                // Delete item when ItemNum reaches 0 to free up the slot
                client.Character.Storage.GetStorage(DestinationStorageType).SetItem(null, 0, slotNo);
                Server.Database.DeleteStorageItem(client.Character.ContentCharacterId, DestinationStorageType, slotNo);
            }
            else
            {
                client.Character.Storage.GetStorage(DestinationStorageType).SetItem(item, itemNum, slotNo);
                Server.Database.ReplaceStorageItem(client.Character.ContentCharacterId, DestinationStorageType, slotNo, itemNum, item);
            }

            client.Enqueue(ntc, queue);

            // Lantern start NTC
            // TODO: Figure out all item IDs that do lantern stuff
            if (item.ItemId == 55)
            {
                // client.Send(SelectedDump.lantern2_27_16);
                // TODO: Start a timer to estinguish after LanternBurnTimeInSeconds expires
                client.Character.IsLanternLit = true;
                client.Enqueue(new S2CCharacterStartLanternNtc() { RemainTime = _Server.GameSettings.GameServerSettings.LanternBurnTimeInSeconds }, queue);
                // client.Party.SendToAllExcept(new S2CCharacterStartLanternOtherNtc() { CharacterId = client.Character.CharacterId }, client);
            }

            return queue;
        }
    }
}

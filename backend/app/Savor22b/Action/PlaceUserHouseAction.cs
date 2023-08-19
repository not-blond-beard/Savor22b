namespace Savor22b.Action;

using System.Collections.Immutable;
using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Assets;
using Libplanet.Headless.Extensions;
using Libplanet.State;
using Savor22b.Action.Exceptions;
using Savor22b.Action.Util;
using Savor22b.Constants;
using Savor22b.Model;
using Savor22b.States;

[ActionType(nameof(PlaceUserHouseAction))]
public class PlaceUserHouseAction : SVRAction
{
    public int VillageID;
    public int TargetX;
    public int TargetY;

    public PlaceUserHouseAction(int villageID, int targetX, int targetY)
    {
        VillageID = villageID;
        TargetX = targetX;
        TargetY = targetY;
    }

    protected override IImmutableDictionary<string, IValue> PlainValueInternal =>
        new Dictionary<string, IValue>()
        {
            [nameof(VillageID)] = VillageID.Serialize(),
            [nameof(TargetX)] = TargetX.Serialize(),
            [nameof(TargetY)] = TargetY.Serialize(),
        }.ToImmutableDictionary();

    private static Village GetVillage(int villageID)
    {
        var village = CsvDataHelper.GetVillageByID(villageID);

        if (village == null)
        {
            throw new InvalidVillageException("Invalid village ID");
        }

        return village;
    }

    private static void ValidateReplaceUserHouse(
        long currentBlock,
        RelocationState? relocationState
    )
    {
        if (relocationState is not null && relocationState.IsRelocationInProgress(currentBlock))
        {
            throw new RelocationInProgressException("Relocation in progress");
        }
    }

    private void ValidateValidateHouseTarget()
    {
        Village village = GetVillage(VillageID);

        if (!village.AbleToPlaceHouse(TargetX, TargetY))
        {
            throw new InvalidVillageException("Invalid target position");
        }
    }

    private void ValidateCheckPlacedHouse(GlobalUserHouseState globalUserHouseState)
    {
        string userHouseKey = globalUserHouseState.CreateKey(VillageID, TargetX, TargetY);

        if (globalUserHouseState.CheckPlacedHouse(userHouseKey))
        {
            throw new HouseAlreadyPlacedException("House already placed");
        }
    }

    private void ValidatePlaceHouse(GlobalUserHouseState globalUserHouseState)
    {
        ValidateValidateHouseTarget();
        ValidateCheckPlacedHouse(globalUserHouseState);
    }

    private void PlaceInitialUserHouse(RootState rootState)
    {
        rootState.SetVillageState(
            new VillageState(new HouseState(VillageID, TargetX, TargetY, new KitchenState()))
        );
    }

    private void ReplaceUserHouse(
        RootState rootState,
        GlobalUserHouseState globalUserHouseState,
        long currentBlock
    )
    {
        Village originVillage = GetVillage(rootState.VillageState!.HouseState.VillageID);
        Village targetVillage = GetVillage(VillageID);

        string prevUserHouseKey = globalUserHouseState.CreateKey(
            rootState.VillageState!.HouseState.VillageID,
            rootState.VillageState.HouseState.PositionX,
            rootState.VillageState.HouseState.PositionY
        );
        int durationBlock = VillageUtil.CalculateReplaceUserHouseBlock(
            originVillage.WorldX,
            originVillage.WorldY,
            targetVillage.WorldX,
            targetVillage.WorldY
        );

        RelocationState relocationState = new RelocationState(
            currentBlock,
            durationBlock,
            VillageID,
            TargetX,
            TargetY
        );

        globalUserHouseState.UserHouse.Remove(prevUserHouseKey);

        rootState.SetRelocationState(relocationState);
        rootState.SetVillageState(
            new VillageState(new HouseState(VillageID, TargetX, TargetY, new KitchenState()))
        );
    }

    public override IAccountStateDelta Execute(IActionContext ctx)
    {
        IAccountStateDelta states = ctx.PreviousStates;

        GlobalUserHouseState globalUserHouseState = states.GetState(Addresses.UserHouseDataAddress)
            is Dictionary stateEncoded
            ? new GlobalUserHouseState(stateEncoded)
            : new GlobalUserHouseState();

        RootState rootState = states.GetState(ctx.Signer) is Dictionary rootStateEncoded
            ? new RootState(rootStateEncoded)
            : new RootState();

        bool isInitialPlaceHouse = rootState.VillageState is null;

        ValidatePlaceHouse(globalUserHouseState);

        if (isInitialPlaceHouse)
        {
            PlaceInitialUserHouse(rootState);
        }
        else
        {
            Village originVillage = GetVillage(rootState.VillageState!.HouseState.VillageID);
            Village targetVillage = GetVillage(VillageID);

            ValidateReplaceUserHouse(ctx.BlockIndex, rootState.RelocationState);
            ReplaceUserHouse(rootState, globalUserHouseState, ctx.BlockIndex);

            FungibleAssetValue price = CalculatePrice.CalculateReplaceUserHousePrice(
                originVillage.WorldX,
                originVillage.WorldY,
                targetVillage.WorldX,
                targetVillage.WorldY
            );

            states = states.TransferAsset(
                ctx.Signer,
                Addresses.ShopVaultAddress,
                price,
                allowNegativeBalance: false
            );
        }

        globalUserHouseState.SetUserHouse(
            globalUserHouseState.CreateKey(VillageID, TargetX, TargetY),
            ctx.Signer
        );

        states = states.SetState(Addresses.UserHouseDataAddress, globalUserHouseState.Serialize());
        states = states.SetState(ctx.Signer, rootState.Serialize());

        return states;
    }

    protected override void LoadPlainValueInternal(IImmutableDictionary<string, IValue> plainValue)
    {
        VillageID = plainValue[nameof(VillageID)].ToInteger();
        TargetX = plainValue[nameof(TargetX)].ToInteger();
        TargetY = plainValue[nameof(TargetY)].ToInteger();
    }
}

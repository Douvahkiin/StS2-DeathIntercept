using System;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace DeathIntercept.Patches;

[HarmonyPatch]
public static class DeathInterceptPatches
{
    private static bool _isGivingUp;
    private static SerializableRun? _cachedSerializableRun;

    /// <summary>
    /// Block the death BGM (play-pause-play) until the player actually chooses Give Up.
    /// </summary>
    [HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayMusic))]
    [HarmonyPrefix]
    private static bool Prefix_PlayMusic(string music)
    {
        if (!_isGivingUp
            && CombatManager.Instance.IsAboutToLose
            && music == "event:/temp/sfx/game_over")
        {
            Log.Info("[DeathIntercept] Suppressing death BGM until player decides.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Block OnEnded(false) from updating progress and run history when the player
    /// died in combat. On retry, these updates would leave behind a fake "loss" record.
    /// </summary>
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
    [HarmonyPrefix]
    private static bool Prefix_OnEnded(bool isVictory)
    {
        if (_isGivingUp)
            return true;

        if (!isVictory && CombatManager.Instance.IsAboutToLose)
        {
            Log.Info("[DeathIntercept] Blocking OnEnded(false) to prevent fake loss record.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// When the player dies in combat, RunManager.OnEnded(false) calls DeleteCurrentRun()
    /// to delete the auto-save. We intercept here: if the player died in combat,
    /// skip deletion so the save can be used for a retry.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteCurrentRun))]
    [HarmonyPrefix]
    private static bool Prefix_DeleteCurrentRun()
    {
        if (_isGivingUp)
            return true;

        if (CombatManager.Instance.IsAboutToLose)
        {
            Log.Info("[DeathIntercept] Combat death detected - preserving run save for potential retry.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Intercepts the Game Over screen. If the player died in combat, shows a retry
    /// dialog instead. On retry, reloads the combat directly from the pre-combat save.
    /// On give up, deletes the save and proceeds normally.
    ///
    /// This runs AFTER CreatureCmd.Kill has completed all its death-processing logic
    /// (including Bottled Fairy / Lizard Tail / other ShouldDie hooks), so the
    /// IsDead check is accurate by this point.
    /// </summary>
    [HarmonyPatch(typeof(NRun), nameof(NRun.ShowGameOverScreen))]
    [HarmonyPrefix]
    private static bool Prefix_ShowGameOverScreen(NRun __instance, SerializableRun serializableRun)
    {
        if (_isGivingUp)
        {
            _isGivingUp = false;
            return true;
        }

        if (!CombatManager.Instance.IsAboutToLose)
            return true;

        // OnEnded was blocked, so serializableRun may be null. Load from save.
        if (serializableRun == null)
        {
            var saveResult = SaveManager.Instance.LoadRunSave();
            if (saveResult.Success && saveResult.SaveData != null)
                serializableRun = saveResult.SaveData;
        }

        Log.Info("[DeathIntercept] All players dead - showing retry dialog.");
        _cachedSerializableRun = serializableRun;
        ShowRetryDialog(__instance);
        return false;
    }

    private static void ShowRetryDialog(Node parent)
    {
        var dialog = new AcceptDialog
        {
            Title = "战败 / Defeat",
            DialogText = "你被击败了。\nYou have been defeated.",
            OkButtonText = "重打 / Retry"
        };
        var cancelBtn = dialog.AddCancelButton("放弃 / Give Up");

        dialog.Confirmed += () =>
        {
            Log.Info("[DeathIntercept] Player chose to retry - reloading combat directly.");
            dialog.QueueFree();
            TaskHelper.RunSafely(ReloadCombatAsync());
        };

        dialog.Canceled += () =>
        {
            Log.Info("[DeathIntercept] Player chose to give up.");
            dialog.QueueFree();
            _isGivingUp = true;

            NAudioManager.Instance?.PlayMusic("event:/temp/sfx/game_over");
            SerializableRun sr = RunManager.Instance.OnEnded(isVictory: false);
            SaveManager.Instance!.DeleteCurrentRun();
            NRun.Instance!.ShowGameOverScreen(sr);
        };

        parent.AddChild(dialog);
        dialog.PopupCentered();

        Button? okBtn = dialog.GetOkButton();
        if (okBtn != null)
            okBtn.SelfModulate = new Color(0.5f, 1f, 0.5f);
        if (cancelBtn != null)
            cancelBtn.SelfModulate = new Color(1f, 0.5f, 0.5f);
    }

    private static async Task ReloadCombatAsync()
    {
        try
        {
            Log.Info("[DeathIntercept] Loading run save for reload...");

            await WaitForPendingSave();

            ReadSaveResult<SerializableRun> loadResult = SaveManager.Instance.LoadRunSave();
            SerializableRun save = loadResult.SaveData
                ?? throw new InvalidOperationException("[DeathIntercept] Run save data was null.");
            RunState runState = RunState.FromSerializable(save);

            NGame game = NGame.Instance
                ?? throw new InvalidOperationException("[DeathIntercept] NGame.Instance was null.");

            NRunMusicController.Instance?.StopMusic();
            await game.Transition.FadeOut(0.3f);

            RunManager.Instance.CleanUp();

            RunManager.Instance.SetUpSavedSinglePlayer(runState, save);
            SfxCmd.Play(runState.Players[0].Character.CharacterTransitionSfx);
            game.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
            await game.LoadRun(runState, save.PreFinishedRoom);

            Log.Info("[DeathIntercept] Combat reloaded successfully.");
        }
        catch (Exception ex)
        {
            Log.Error($"[DeathIntercept] Error during combat reload: {ex}");
            try
            {
                await NGame.Instance!.ReturnToMainMenu();
            }
            catch (Exception ex2)
            {
                Log.Error($"[DeathIntercept] Failed to return to main menu: {ex2}");
            }
        }
    }

    private static async Task WaitForPendingSave()
    {
        Task? currentSave = SaveManager.Instance.CurrentRunSaveTask;
        if (currentSave != null)
        {
            Log.Info("[DeathIntercept] Waiting for pending save to complete...");
            try
            {
                await currentSave;
            }
            catch (Exception ex)
            {
                Log.Error($"[DeathIntercept] Save task failed while waiting: {ex}");
            }
        }
    }
}

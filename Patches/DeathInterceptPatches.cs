using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
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
    private static bool _allowGameOver;
    private static IReadOnlyCollection<Creature>? _cachedCreatures;
    private static bool _cachedForce;

    /// <summary>
    /// Intercept BEFORE the BGM code runs. CreatureCmd.Kill checks all-players-dead
    /// and then calls LoseCombat → StopMusic → PlayMusic (death BGM) → OnEnded → ShowGameOverScreen.
    /// By returning false here, we skip the entire chain, so the combat BGM keeps playing.
    /// </summary>
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill),
        typeof(IReadOnlyCollection<Creature>), typeof(bool))]
    [HarmonyPrefix]
    private static bool Prefix_Kill(IReadOnlyCollection<Creature> creatures, bool force)
    {
        if (_allowGameOver)
            return true;

        var runState = creatures.FirstOrDefault(c => c.IsPlayer)?.Player?.RunState;
        if (runState == null || !runState.Players.All(p => p.Creature.IsDead))
            return true;

        if (!CombatManager.Instance.IsInProgress)
            return true;

        Log.Info("[DeathIntercept] All players dead - intercepting before BGM changes.");
        _cachedCreatures = creatures;
        _cachedForce = force;
        ShowRetryDialog();
        return false;
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
        if (_allowGameOver)
            return true;

        if (CombatManager.Instance.IsAboutToLose)
        {
            Log.Info("[DeathIntercept] Combat death detected - preserving run save for potential retry.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Fallback intercept for ShowGameOverScreen. Normally the Kill Prefix handles
    /// the interception, but if something bypasses it, this catches and shows the dialog.
    /// </summary>
    [HarmonyPatch(typeof(NRun), nameof(NRun.ShowGameOverScreen))]
    [HarmonyPrefix]
    private static bool Prefix_ShowGameOverScreen(NRun __instance, SerializableRun serializableRun)
    {
        if (_allowGameOver)
        {
            _allowGameOver = false;
            return true;
        }

        if (!CombatManager.Instance.IsAboutToLose)
            return true;

        Log.Info("[DeathIntercept] Fallback: showing retry dialog at ShowGameOverScreen.");
        ShowRetryDialog();
        return false;
    }

    private static void ShowRetryDialog()
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
            _allowGameOver = true;
            TaskHelper.RunSafely(CreatureCmd.Kill(_cachedCreatures!, _cachedForce));
        };

        var sceneTree = (SceneTree)Engine.GetMainLoop();
        sceneTree.Root.AddChild(dialog);
        dialog.PopupCentered();

        // Color buttons after they are in the scene tree
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

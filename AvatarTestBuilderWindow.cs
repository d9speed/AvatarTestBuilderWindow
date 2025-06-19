using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDKBase.Editor;
using VRC.SDK3A.Editor;
using VRC.SDK3.Avatars.Components;

public class AvatarTestBuilderWindow : EditorWindow
{
    private List<GameObject> avatars = new List<GameObject>();
    private Vector2 scrollPos;

    // 同時ビルドを防ぐセマフォ
    private static readonly SemaphoreSlim BuildSemaphore = new SemaphoreSlim(1, 1);

    private const string Prefix       = "[Test avatar build]";
    private const int    IntervalMs   = 10000; // 10秒待機

    [MenuItem("Tools/Avatar Test Builder")]
    public static void ShowWindow() => GetWindow<AvatarTestBuilderWindow>("Avatar Test Builder");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Avatar Test Builder", EditorStyles.boldLabel);
        if (GUILayout.Button("Add Avatar")) avatars.Add(null);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        for (int i = 0; i < avatars.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            avatars[i] = (GameObject)EditorGUILayout.ObjectField(avatars[i], typeof(GameObject), true);
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                avatars.RemoveAt(i);
                i--;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("Build Selected Avatars"))
            _ = BuildSelectedAvatarsAsync();
    }

    private async Task BuildSelectedAvatarsAsync()
    {
        // 同時実行防止
        if (!BuildSemaphore.Wait(0))
        {
            Debug.LogError("ビルド処理が既に実行中です。終了を待ってください。");
            return;
        }

        try
        {
            Debug.Log($"=== Start batch build ({avatars.Count} avatars) ===");

            for (int i = 0; i < avatars.Count; i++)
            {
                var avatar = avatars[i];
                Debug.Log($"--- Avatar [{i}] start ---");

                // null/ルート階層/Descriptor チェック
                if (avatar == null)
                {
                    Debug.LogWarning($"[{i}] が null です。スキップします。");
                    continue;
                }
                if (avatar.transform.parent != null)
                {
                    Debug.LogWarning($"[{i}] {avatar.name} はルート階層にありません。スキップします。");
                    continue;
                }
                if (avatar.GetComponent<VRCAvatarDescriptor>() == null)
                {
                    Debug.LogWarning($"[{i}] {avatar.name} に VRCAvatarDescriptor がありません。スキップします。");
                    continue;
                }

                // ビルダーを都度取得
                Debug.Log($"[{i}] TryGetBuilder...");
                if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var builder) || builder == null)
                {
                    Debug.LogError($"[{i}] ビルダーの取得に失敗しました。SDKパネルを開き直してください。");
                    break;
                }

                // コントロールパネルのエラー状態をリセット
                var panel = VRCSdkControlPanel.window;
                if (panel)
                {
                    panel.ResetIssues();
                    panel.CheckedForIssues = true;
                    Debug.Log($"[{i}] ControlPanel issues reset.");
                }

            
                // 名前のプレフィックス処理
                string originalName = avatar.name;
                if (!originalName.StartsWith(Prefix))
                {
                    avatar.name = Prefix + originalName;
                    EditorUtility.SetDirty(avatar);
                    Debug.Log($"[{i}] 名前を \"{avatar.name}\" に変更");
                }

                // ビルド開始ログ
                Debug.Log($"[{i}] [Build Start] {avatar.name}");
                try
                {
                    await builder.BuildAndTest(avatar);
                    Debug.Log($"[{i}] [Build Success] {avatar.name}");
                    // 成功時はプレフィックスを維持
                }
                catch (Exception e)
                {
                    // 失敗時は元の名前に戻す
                    avatar.name = originalName;
                    EditorUtility.SetDirty(avatar);
                    Debug.LogError($"[{i}] [Build Failed] {avatar.name} : {e.Message}");
                }

                // シーン保存＋インターバル
                EditorSceneManager.SaveOpenScenes();
                Debug.Log($"[{i}] シーンを保存しました。");
                Debug.Log($"[{i}] {IntervalMs/1000} 秒待機します…");
                await Task.Delay(IntervalMs);
                Debug.Log($"[{i}] インターバル完了");
            }

            Debug.Log("=== Batch build complete ===");
        }
        finally
        {
            BuildSemaphore.Release();
        }
    }


}

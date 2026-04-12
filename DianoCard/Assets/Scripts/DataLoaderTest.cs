using DianoCard.Data;
using UnityEngine;

/// <summary>
/// 빈 게임오브젝트에 붙이고 Play 눌러서 Console에 로드 결과 확인.
/// </summary>
public class DataLoaderTest : MonoBehaviour
{
    void Start()
    {
        DataManager.Instance.Load();

        // 샘플 출력
        var triceratops = DataManager.Instance.GetCard("C001");
        if (triceratops != null)
        {
            Debug.Log($"[Test] C001 = {triceratops.nameKr} / ATK:{triceratops.attack} HP:{triceratops.hp} / {triceratops.description}");
        }

        var boss = DataManager.Instance.GetEnemy("E901");
        if (boss != null)
        {
            Debug.Log($"[Test] E901 = {boss.nameKr} / HP:{boss.hp} ATK:{boss.attack} / {boss.description}");
        }

        var chapter1 = DataManager.Instance.GetChapter("CH01");
        if (chapter1 != null)
        {
            Debug.Log($"[Test] CH01 = {chapter1.nameKr} / mana:{chapter1.mana} / normal pool: {string.Join(",", chapter1.normalEnemyPool)}");
        }
    }
}

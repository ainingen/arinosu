using UnityEngine;

/// <summary>土を置こうとした結果。</summary>
public enum MoundPlacementResult
{
    /// <summary>置けた</summary>
    Ok,
    /// <summary>世界の外</summary>
    OutsideWorld,
    /// <summary>巣の入口に近すぎる</summary>
    NearEntrance,
    /// <summary>その列に餌が乗っている</summary>
    FoodOnColumn,
    /// <summary>その列に地面がない（積む先がない）</summary>
    NoGround,
    /// <summary>準備ができていない</summary>
    NotReady,
}

/// <summary>
/// 運び出した土を地表に積んで塚を作る（行動モデル.md 12-5）。
///
/// 積む処理をここ1か所にまとめてある。
/// 安息角の判定（1粒が転がり落ちる）と、埋まったアリ・餌の押し上げもここで行う。
/// </summary>
[DisallowMultipleComponent]
public class MoundBuilder : MonoBehaviour
{
    [SerializeField] private SoilGrid grid;
    [SerializeField] private NestField nestField;
    [SerializeField] private AntSettings settings;
    [SerializeField] private Colony colony;

    private void Awake()
    {
        if (grid == null) grid = FindFirstObjectByType<SoilGrid>();
        if (nestField == null) nestField = FindFirstObjectByType<NestField>();
        if (colony == null) colony = FindFirstObjectByType<Colony>();
    }

    /// <summary>
    /// その場所の列に土を1粒積む。
    /// 積んだあと、安息角に従って低いほうへ転がし、
    /// 最後に落ち着いた列で、埋まったアリと餌を押し上げる。
    /// </summary>
    public bool TryPlaceSoil(Vector2 worldPosition, out MoundPlacementResult result)
    {
        if (grid == null || settings == null)
        {
            result = MoundPlacementResult.NotReady;
            return false;
        }

        int x, y;
        if (!grid.WorldToCell(worldPosition, out x, out y))
        {
            result = MoundPlacementResult.OutsideWorld;
            return false;
        }

        if (!CanPlaceOnColumn(x, out result)) return false;

        int top = FindGroundTop(x);
        if (top < 0)
        {
            result = MoundPlacementResult.NoGround;
            return false;
        }

        grid.SetCell(x, top, CellType.Soil);
        int settledColumn = RollDown(x);

        PushUpOccupants(settledColumn);
        if (settledColumn != x) PushUpOccupants(x);

        if (colony != null) colony.ReportMound();
        result = MoundPlacementResult.Ok;
        return true;
    }

    /// <summary>その列に積んでよいか（入口と餌を埋めないための判定）。</summary>
    private bool CanPlaceOnColumn(int x, out MoundPlacementResult result)
    {
        if (nestField != null && nestField.HasEntrance)
        {
            int ex, ey;
            if (grid.WorldToCell(nestField.EntranceWorld, out ex, out ey)
                && Mathf.Abs(x - ex) <= settings.entranceKeepClear)
            {
                result = MoundPlacementResult.NearEntrance;
                return false;
            }
        }

        var foods = FoodSource.All;
        for (int i = 0; i < foods.Count; i++)
        {
            if (foods[i] == null) continue;
            int fx, fy;
            if (!grid.WorldToCell(foods[i].Position, out fx, out fy)) continue;
            if (fx != x) continue;
            result = MoundPlacementResult.FoodOnColumn;
            return false;
        }

        result = MoundPlacementResult.Ok;
        return true;
    }

    /// <summary>
    /// 安息角。隣より高くなりすぎていたら、低いほうへ1粒移す。
    /// 移した先でも同じ判定を繰り返す（転がり落ちる）。落ち着いた列を返す。
    /// </summary>
    private int RollDown(int x)
    {
        int current = x;

        for (int roll = 0; roll < settings.moundMaxRoll; roll++)
        {
            int height = ColumnHeight(current);
            int leftHeight = ColumnHeight(current - 1);
            int rightHeight = ColumnHeight(current + 1);

            // 低いほうの隣を選ぶ
            int target = leftHeight <= rightHeight ? current - 1 : current + 1;
            int targetHeight = Mathf.Min(leftHeight, rightHeight);

            if (height - targetHeight < settings.moundMaxStep) break;
            if (!grid.IsInside(target, 0)) break;

            // 転がった先にも入口・餌のガードを適用する
            MoundPlacementResult guard;
            if (!CanPlaceOnColumn(target, out guard)) break;

            int from = FindTopSoil(current);
            int to = FindGroundTop(target);
            if (from < 0 || to < 0) break;

            grid.SetCell(current, from, CellType.Air);
            grid.SetCell(target, to, CellType.Soil);
            current = target;
        }

        return current;
    }

    /// <summary>その列の地面のいちばん上のマス（積む先＝空気のマス）。見つからなければ -1。</summary>
    private int FindGroundTop(int x)
    {
        for (int y = grid.Height - 2; y >= 0; y--)
        {
            if (!grid.IsSolid(x, y)) continue;
            int above = y + 1;
            if (!grid.IsInside(x, above)) return -1;
            if (!grid.IsPassable(x, above)) return -1;
            return above;
        }
        return -1;
    }

    /// <summary>その列のいちばん上にある土のマス。見つからなければ -1。</summary>
    private int FindTopSoil(int x)
    {
        for (int y = grid.Height - 1; y >= 0; y--)
        {
            if (grid.GetCell(x, y) == CellType.Soil) return y;
        }
        return -1;
    }

    /// <summary>その列の地面の高さ（いちばん上の固体マスの y）。</summary>
    private int ColumnHeight(int x)
    {
        if (!grid.IsInside(x, 0)) return int.MaxValue;   // 世界の外は「高い」扱いにして転がさない
        for (int y = grid.Height - 1; y >= 0; y--)
        {
            if (grid.IsSolid(x, y)) return y;
        }
        return 0;
    }

    /// <summary>
    /// 土に埋まってしまったアリと餌を、その列の地面の上へ押し上げる。
    /// </summary>
    private void PushUpOccupants(int x)
    {
        int top = FindGroundTop(x);
        if (top < 0) return;

        Vector2 standing = grid.CellToWorld(x, top);

        var ants = Ant.All;
        for (int i = 0; i < ants.Count; i++)
        {
            Ant ant = ants[i];
            if (ant == null) continue;
            int ax, ay;
            if (!grid.WorldToCell(ant.Position, out ax, out ay)) continue;
            if (ax != x) continue;
            if (grid.IsPassable(ax, ay)) continue;   // まだ土に埋まっていない
            ant.Teleport(standing);
        }

        var foods = FoodSource.All;
        for (int i = 0; i < foods.Count; i++)
        {
            FoodSource food = foods[i];
            if (food == null) continue;
            int fx, fy;
            if (!grid.WorldToCell(food.Position, out fx, out fy)) continue;
            if (fx != x) continue;
            if (fy > top) continue;   // まだ地面より上にある

            // 餌のピボットは粒の底なので、マスの下辺（＝地面の表面）に置き直す
            food.PlaceOnGround(new Vector2(standing.x, standing.y - grid.CellSize * 0.5f));
        }
    }
}

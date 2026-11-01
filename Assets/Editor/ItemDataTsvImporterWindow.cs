using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class ItemDataTsvImporterWindow : EditorWindow
{
    const string Folder = "Assets/Data2/ItemData";
    static readonly string[] Required = { "ItemID", "アイテム名", "説明文", "アイコン", "購入価格", "影響", "効果数値", "使用対象", "持続期間" };
    readonly List<Row> rows = new List<Row>();
    readonly List<string> errors = new List<string>();
    string filePath = "";
    bool ready;
    Vector2 scroll;
    int creates, updates;

    sealed class Row
    {
        public string id, name, description, target, duration;
        public int price;
        public string[] effects;
        public int[] values;
        public Sprite icon;
        public ItemData asset;
    }

    [MenuItem("Tools/Item Data/TSV Importer")]
    static void Open() => GetWindow<ItemDataTsvImporterWindow>("Item TSV Importer");

    void OnGUI()
    {
        EditorGUILayout.LabelField("ItemData TSV Importer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("ItemIDで照合し、Assets/Data2/ItemDataに作成・更新します。TSVにないデータは削除しません。", MessageType.Info);
        EditorGUILayout.BeginHorizontal();
        filePath = EditorGUILayout.TextField("TSV File", filePath);
        if (GUILayout.Button("選択", GUILayout.Width(70)))
        {
            string p = EditorUtility.OpenFilePanel("ItemData TSVを選択", Application.dataPath, "tsv");
            if (!string.IsNullOrEmpty(p)) { filePath = p; ResetPreview(); }
        }
        EditorGUILayout.EndHorizontal();
        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(filePath) || !File.Exists(filePath)))
            if (GUILayout.Button("読み込み・内容確認")) BuildPreview();

        if (!ready && errors.Count == 0) return;
        if (errors.Count > 0)
        {
            EditorGUILayout.HelpBox("エラーがあるため適用できません。", MessageType.Error);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(220));
            foreach (string e in errors) EditorGUILayout.LabelField(e, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
            return;
        }
        EditorGUILayout.HelpBox("更新: " + updates + "件    新規作成: " + creates + "件", MessageType.Info);
        using (new EditorGUI.DisabledScope(!ready || rows.Count == 0))
            if (GUILayout.Button("ItemDataへ適用")) Apply();
    }

    void ResetPreview() { ready = false; rows.Clear(); errors.Clear(); creates = updates = 0; }

    void BuildPreview()
    {
        ResetPreview();
        try
        {
            string text;
            using (StreamReader reader = new StreamReader(filePath, new UTF8Encoding(false), true)) text = reader.ReadToEnd();
            List<List<string>> table = ParseTsv(text);
            if (table.Count < 2) { errors.Add("ヘッダーとデータ行を含むTSVを指定してください。"); return; }
            Dictionary<string,int> col = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            for (int i=0;i<table[0].Count;i++)
            {
                string h=table[0][i].Trim().TrimStart('\uFEFF');
                if (h.Length==0) continue;
                if (col.ContainsKey(h)) errors.Add("ヘッダーが重複しています: "+h); else col.Add(h,i);
            }
            foreach(string h in Required) if(!col.ContainsKey(h)) errors.Add("必須列がありません: "+h);
            if(errors.Count>0) return;

            int max=-1; foreach(string h in Required) max=Math.Max(max,col[h]);
            Dictionary<string,List<ItemData>> existing=FindExisting();
            HashSet<string> ids=new HashSet<string>(StringComparer.Ordinal);
            for(int r=1;r<table.Count;r++)
            {
                List<string> line=table[r];
                if(line.Count==1&&string.IsNullOrWhiteSpace(line[0])) continue;
                int n=r+1;
                if(line.Count<=max) { errors.Add("行 "+n+": 必須列の値が不足しています。"); continue; }
                string id=Get(line,col,"ItemID").Trim(), name=Get(line,col,"アイテム名").Trim();
                if(string.IsNullOrWhiteSpace(id)||!ValidName(id)) { errors.Add("行 "+n+": ItemIDが空、またはAsset名に使えません。"); continue; }
                if(string.IsNullOrWhiteSpace(name)) { errors.Add("行 "+n+": アイテム名が空です。"); continue; }
                if(!ids.Add(id)) { errors.Add("行 "+n+": ItemIDが重複しています: "+id); continue; }
                if(!int.TryParse(Get(line,col,"購入価格").Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out int price)||price<0)
                { errors.Add("行 "+n+": 購入価格は0以上の整数で指定してください。"); continue; }
                string[] effects=Split(Get(line,col,"影響")), nums=Split(Get(line,col,"効果数値"));
                if(effects.Length==0||effects.Length!=nums.Length) { errors.Add("行 "+n+": 影響と効果数値の個数を一致させてください。"); continue; }
                int[] vals=new int[nums.Length]; bool valid=true;
                for(int i=0;i<nums.Length;i++) if(!int.TryParse(nums[i],NumberStyles.Integer,CultureInfo.InvariantCulture,out vals[i]))
                { errors.Add("行 "+n+": 効果数値が整数ではありません: "+nums[i]); valid=false; }
                if(!valid) continue;
                if(!TryIcon(Get(line,col,"アイコン").Trim(),n,out Sprite icon)) continue;
                ItemData asset=null;
                if(existing.TryGetValue(id,out List<ItemData> found))
                { if(found.Count>1) { errors.Add("同じItemIDの既存ItemDataが複数あります: "+id); continue; } asset=found[0]; }
                string assetPath=Folder+"/"+id+".asset";
                UnityEngine.Object occupant=AssetDatabase.LoadMainAssetAtPath(assetPath);
                if(occupant!=null&&occupant!=asset) { errors.Add("同名の別Assetが存在します: "+assetPath); continue; }
                rows.Add(new Row { id=id,name=name,description=Get(line,col,"説明文"),target=Get(line,col,"使用対象").Trim(),
                    duration=Get(line,col,"持続期間").Trim(),price=price,effects=effects,values=vals,icon=icon,asset=asset });
            }
            if(errors.Count>0) { rows.Clear(); return; }
            foreach(Row row in rows) if(row.asset==null) creates++; else updates++;
            ready=true;
        }
        catch(Exception e) { errors.Add("TSVを読み込めませんでした: "+e.Message); }
    }

    void Apply()
    {
        if(!ready||errors.Count>0) return;
        try
        {
            if(!AssetDatabase.IsValidFolder("Assets/Data2")) AssetDatabase.CreateFolder("Assets","Data2");
            if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Data2","ItemData");
            foreach(Row row in rows)
            {
                string path=Folder+"/"+row.id+".asset";
                ItemData d=row.asset;
                if(d==null) { d=CreateInstance<ItemData>(); d.name=row.id; AssetDatabase.CreateAsset(d,path); }
                else
                {
                    Undo.RecordObject(d,"Update ItemData from TSV");
                    string old=AssetDatabase.GetAssetPath(d);
                    if(!string.Equals(old,path,StringComparison.OrdinalIgnoreCase))
                    { string move=AssetDatabase.MoveAsset(old,path); if(!string.IsNullOrEmpty(move)) throw new IOException(move); }
                    d.name=row.id;
                }
                d.itemId=row.id; d.itemName=row.name; d.description=row.description; d.icon=row.icon;
                d.purchasePrice=row.price; d.effects=row.effects; d.effectValues=row.values; d.target=row.target; d.duration=row.duration;
                EditorUtility.SetDirty(d);
            }
        }
        catch(Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("適用エラー","処理中にエラーが発生しました。Consoleを確認してください。","OK"); return; }
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("完了","ItemDataを更新しました。\n更新: "+updates+"件\n新規作成: "+creates+"件","OK");
        BuildPreview();
    }

    Dictionary<string,List<ItemData>> FindExisting()
    {
        Dictionary<string,List<ItemData>> result=new Dictionary<string,List<ItemData>>(StringComparer.Ordinal);
        if(!AssetDatabase.IsValidFolder(Folder)) return result;
        foreach(string guid in AssetDatabase.FindAssets("t:ItemData",new[]{Folder}))
        {
            ItemData d=AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if(d==null||string.IsNullOrWhiteSpace(d.itemId)) continue;
            if(!result.TryGetValue(d.itemId,out List<ItemData> list)) { list=new List<ItemData>(); result.Add(d.itemId,list); }
            list.Add(d);
        }
        return result;
    }

    bool TryIcon(string input,int line,out Sprite sprite)
    {
        sprite=null; string path=input.Trim().Trim('"').Replace('\\','/');
        if(string.IsNullOrWhiteSpace(path)) { errors.Add("行 "+line+": アイコンのパスが空です。"); return false; }
        if(Path.IsPathRooted(path))
        {
            string root=Directory.GetParent(Application.dataPath).FullName.Replace('\\','/').TrimEnd('/');
            string full;
            try { full=Path.GetFullPath(path).Replace('\\','/'); }
            catch { errors.Add("行 "+line+": アイコンのパスが不正です。"); return false; }
            if(!full.StartsWith(root+"/",StringComparison.OrdinalIgnoreCase)) { errors.Add("行 "+line+": アイコンはUnityプロジェクト内を指定してください。"); return false; }
            path=full.Substring(root.Length+1);
        }
        if(!path.StartsWith("Assets/",StringComparison.OrdinalIgnoreCase)) { errors.Add("行 "+line+": アイコンはAssets内の画像を指定してください。"); return false; }
        foreach(UnityEngine.Object a in AssetDatabase.LoadAllAssetsAtPath(path)) if(a is Sprite s) { sprite=s; return true; }
        errors.Add("行 "+line+": 画像をSpriteとして読み込めません: "+path+"。Texture TypeをSpriteにしてください。"); return false;
    }

    string Get(List<string> row,Dictionary<string,int> col,string key) { int i=col[key]; return i<row.Count?row[i]:""; }
    string[] Split(string text) { List<string> list=new List<string>(); foreach(string s in text.Split(',')) if(!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim()); return list.ToArray(); }
    bool ValidName(string s) { return s!="."&&s!=".."&&s.IndexOfAny(Path.GetInvalidFileNameChars())<0&&s.IndexOf('/')<0&&s.IndexOf('\\')<0; }

    List<List<string>> ParseTsv(string text)
    {
        List<List<string>> rows=new List<List<string>>(); List<string> row=new List<string>(); StringBuilder cell=new StringBuilder(); bool quoted=false;
        for(int i=0;i<text.Length;i++)
        {
            char c=text[i];
            if(c=='"') { if(quoted&&i+1<text.Length&&text[i+1]=='"') { cell.Append('"'); i++; } else quoted=!quoted; }
            else if(!quoted&&c=='\t') { row.Add(cell.ToString()); cell.Clear(); }
            else if(!quoted&&(c=='\r'||c=='\n'))
            { if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n') i++; row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row=new List<string>(); }
            else cell.Append(c);
        }
        if(quoted) throw new FormatException("閉じていない引用符があります。");
        if(cell.Length>0||row.Count>0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
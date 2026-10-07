using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using StudyJourney.Avalonia.Models.GradeAnalysis;

namespace StudyJourney.Avalonia.Services.GradeAnalysis;

/// <summary>
/// 班级成绩库（SQLite）。文件放在**程序目录**下的 <c>grades.db</c>，与 settings.json /
/// schedule.json 同级 —— 整文件夹拷贝即完成迁移，符合本项目的分发约定。
/// <para><b>为什么用 SQLite 而不是 JSON</b>：成绩表是「学生 × 考试 × 科目」的三维数据，
/// 一个班一学期就是几千行。JSON 方案每次写回都要整份序列化，且无法做「按考试取成绩」这类
/// 局部查询；更关键的是成员快照（GroupMember）需要按考试顺序做区间查询，关系型天然合适。</para>
/// <para><b>AOT 纪律</b>：本类只使用参数化 SQL + <c>SqliteDataReader</c> 的强类型取值，
/// **不涉及任何反射/ORM**。Microsoft.Data.Sqlite 本身是 ADO.NET 门 + SQLitePCLRaw 的
/// DllImport 封装（原生 e_sqlite3 随包分发），AOT 下无需动态代码。</para>
/// <para><b>表结构变更纪律</b>：靠 <c>PRAGMA user_version</c> 做版本号，迁移只允许**追加**
/// （加表 / 加列 / 加索引），不允许删列改类型 —— 老师的机器上已经有真实数据了。</para>
/// </summary>
public sealed class GradeDatabase
{
    /// <summary>当前 schema 版本。改表结构时 +1，并在 <see cref="Migrate"/> 里补一段迁移。</summary>
    private const int SchemaVersion = 1;

    private readonly string _connectionString;

    public string DbPath { get; }

    public GradeDatabase(string? dbPath = null)
    {
        DbPath = dbPath ?? Path.Combine(AppContext.BaseDirectory, "grades.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // 外键约束默认关（SQLite 的历史包袱），显式打开，避免孤儿成绩行。
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }
        return conn;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  初始化与迁移
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>建库建表（幂等）。返回是否新建了数据库文件，供界面提示「已创建成绩库」。</summary>
    public bool Initialize()
    {
        bool isNew = !File.Exists(DbPath);
        var dir = Path.GetDirectoryName(DbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var conn = Open();
        int version = GetUserVersion(conn);
        if (version == 0)
        {
            CreateSchema(conn);
            SetUserVersion(conn, SchemaVersion);
        }
        else if (version < SchemaVersion)
        {
            Migrate(conn, version);
            SetUserVersion(conn, SchemaVersion);
        }
        return isNew;
    }

    private static int GetUserVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    private static void SetUserVersion(SqliteConnection conn, int version)
    {
        using var cmd = conn.CreateCommand();
        // PRAGMA 不支持参数占位符，version 是编译期常量，无注入面。
        cmd.CommandText = $"PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }

    private static void CreateSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS students (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                student_no      TEXT    NOT NULL,
                name            TEXT    NOT NULL,
                surname_strokes INTEGER NOT NULL DEFAULT 0,
                given_strokes   INTEGER NOT NULL DEFAULT 0,
                class_name      TEXT    NOT NULL DEFAULT ''
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_students_no ON students(student_no);

            CREATE TABLE IF NOT EXISTS exams (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                name             TEXT    NOT NULL,
                exam_date        TEXT    NOT NULL,
                grade_total      INTEGER NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_exams_name_date ON exams(name, exam_date);

            CREATE TABLE IF NOT EXISTS scores (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                student_id INTEGER NOT NULL REFERENCES students(id) ON DELETE CASCADE,
                exam_id    INTEGER NOT NULL REFERENCES exams(id)    ON DELETE CASCADE,
                subject    TEXT    NOT NULL,
                score      REAL    NOT NULL DEFAULT 0,
                full_score REAL    NOT NULL DEFAULT 0,
                status     INTEGER NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_scores ON scores(student_id, exam_id, subject);

            -- 年级排名是「录入值」：本模块只掌握本班名单，算不出年级名次。
            CREATE TABLE IF NOT EXISTS grade_ranks (
                student_id INTEGER NOT NULL REFERENCES students(id) ON DELETE CASCADE,
                exam_id    INTEGER NOT NULL REFERENCES exams(id)    ON DELETE CASCADE,
                grade_rank INTEGER NOT NULL,
                PRIMARY KEY (student_id, exam_id)
            );

            CREATE TABLE IF NOT EXISTS groups (
                id    INTEGER PRIMARY KEY AUTOINCREMENT,
                name  TEXT NOT NULL,
                color TEXT NOT NULL DEFAULT '#2B6CB0'
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_groups_name ON groups(name);

            -- 成员快照：一条 = 「自 effective_exam_id 起该生属于该组」。换人只追加，不改旧行。
            CREATE TABLE IF NOT EXISTS group_members (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                group_id          INTEGER NOT NULL REFERENCES groups(id)   ON DELETE CASCADE,
                student_id        INTEGER NOT NULL REFERENCES students(id) ON DELETE CASCADE,
                effective_exam_id INTEGER NOT NULL REFERENCES exams(id)    ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_group_members ON group_members(group_id, student_id, effective_exam_id);

            -- 模块自己的键值配置（视图列、权重、阈值），值统一是 JSON 字符串。
            CREATE TABLE IF NOT EXISTS app_kv (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>增量迁移。version 是旧的 user_version。</summary>
    private static void Migrate(SqliteConnection conn, int version)
    {
        // 示例（将来加列时照抄）：
        //   if (version < 2) { Exec(conn, "ALTER TABLE exams ADD COLUMN remark TEXT NOT NULL DEFAULT ''"); }
        // 目前只有 v1，无迁移。
        _ = conn;
        _ = version;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  学生
    // ────────────────────────────────────────────────────────────────────────

    public List<Student> GetStudents()
    {
        var list = new List<Student>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, student_no, name, surname_strokes, given_strokes, class_name FROM students ORDER BY id;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Student
            {
                Id = r.GetInt64(0),
                StudentNo = r.GetString(1),
                Name = r.GetString(2),
                SurnameStrokes = r.GetInt32(3),
                GivenNameStrokes = r.GetInt32(4),
                ClassName = r.GetString(5),
            });
        }
        return list;
    }

    /// <summary>按学号 upsert 学生，返回自增 id。<b>学号是自然主键</b> —— 同一学号反复导入不应产生重复学生。</summary>
    public long UpsertStudent(SqliteConnection conn, SqliteTransaction tx, Student s)
    {
        // ⚠ 刻意拆成「写 + 读」两条命令，而不是一条 "INSERT ...; SELECT id;"：
        //   多语句命令下 ExecuteScalar 取的是**第一条语句**的结果集，而 INSERT 没有行，
        //   于是会拿到 null。这类坑不会报错，只会让所有导入的 id 变成 0。
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO students (student_no, name, surname_strokes, given_strokes, class_name)
                VALUES ($no, $name, $ss, $gs, $cls)
                ON CONFLICT(student_no) DO UPDATE SET
                    name            = excluded.name,
                    surname_strokes = CASE WHEN excluded.surname_strokes > 0 THEN excluded.surname_strokes ELSE students.surname_strokes END,
                    given_strokes   = CASE WHEN excluded.given_strokes   > 0 THEN excluded.given_strokes   ELSE students.given_strokes   END,
                    class_name      = CASE WHEN excluded.class_name <> '' THEN excluded.class_name ELSE students.class_name END;
                """;
            cmd.Parameters.AddWithValue("$no", s.StudentNo);
            cmd.Parameters.AddWithValue("$name", s.Name);
            cmd.Parameters.AddWithValue("$ss", s.SurnameStrokes);
            cmd.Parameters.AddWithValue("$gs", s.GivenNameStrokes);
            cmd.Parameters.AddWithValue("$cls", s.ClassName);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id FROM students WHERE student_no = $no;";
            cmd.Parameters.AddWithValue("$no", s.StudentNo);
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }
    }

    public void UpdateStudent(Student s)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE students SET name = $name, surname_strokes = $ss, given_strokes = $gs, class_name = $cls
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$name", s.Name);
        cmd.Parameters.AddWithValue("$ss", s.SurnameStrokes);
        cmd.Parameters.AddWithValue("$gs", s.GivenNameStrokes);
        cmd.Parameters.AddWithValue("$cls", s.ClassName);
        cmd.Parameters.AddWithValue("$id", s.Id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteStudent(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM students WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  考试
    // ────────────────────────────────────────────────────────────────────────

    public List<Exam> GetExams()
    {
        var list = new List<Exam>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        // 按日期 + id 排序：Analysis 层的「考试顺序」就是基于这个顺序，
        // 因此「上一次考试」的语义是稳定的（同一天多场按录入先后）。
        cmd.CommandText = "SELECT id, name, exam_date, grade_total FROM exams ORDER BY exam_date, id;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Exam
            {
                Id = r.GetInt64(0),
                Name = r.GetString(1),
                ExamDate = ParseDate(r.GetString(2)),
                GradeTotalCount = r.GetInt32(3),
            });
        }
        return list;
    }

    public long UpsertExam(SqliteConnection conn, SqliteTransaction tx, Exam e)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO exams (name, exam_date, grade_total)
                VALUES ($name, $date, $gt)
                ON CONFLICT(name, exam_date) DO UPDATE SET
                    grade_total = CASE WHEN excluded.grade_total > 0 THEN excluded.grade_total ELSE exams.grade_total END;
                """;
            cmd.Parameters.AddWithValue("$name", e.Name);
            cmd.Parameters.AddWithValue("$date", FormatDate(e.ExamDate));
            cmd.Parameters.AddWithValue("$gt", e.GradeTotalCount);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id FROM exams WHERE name = $name AND exam_date = $date;";
            cmd.Parameters.AddWithValue("$name", e.Name);
            cmd.Parameters.AddWithValue("$date", FormatDate(e.ExamDate));
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }
    }

    public void UpdateExamMeta(long examId, string name, DateTime date, int gradeTotal)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE exams SET name = $name, exam_date = $date, grade_total = $gt WHERE id = $id;";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$date", FormatDate(date));
        cmd.Parameters.AddWithValue("$gt", gradeTotal);
        cmd.Parameters.AddWithValue("$id", examId);
        cmd.ExecuteNonQuery();
    }

    public void DeleteExam(long examId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM exams WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", examId);
        cmd.ExecuteNonQuery();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  成绩
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>取某场考试的全部成绩行（键 = (studentId, subject)）。</summary>
    public List<ScoreRecord> GetScoresForExam(long examId)
    {
        var list = new List<ScoreRecord>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, student_id, exam_id, subject, score, full_score, status
            FROM scores WHERE exam_id = $eid;
            """;
        cmd.Parameters.AddWithValue("$eid", examId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ScoreRecord
            {
                Id = r.GetInt64(0),
                StudentId = r.GetInt64(1),
                ExamId = r.GetInt64(2),
                Subject = r.GetString(3),
                Score = r.GetDouble(4),
                FullScore = r.GetDouble(5),
                Status = (ScoreStatus)r.GetInt32(6),
            });
        }
        return list;
    }

    /// <summary>取成绩全表（跨考试）。成绩分析是「整学期」视角，一次读全表比按考试循环查更省心；
    /// 一个班一学期约 6 科 × 6 场 × 50 人 ≈ 1800 行，量级完全不必优化。</summary>
    public List<ScoreRecord> GetAllScores()
    {
        var list = new List<ScoreRecord>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, student_id, exam_id, subject, score, full_score, status FROM scores;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ScoreRecord
            {
                Id = r.GetInt64(0),
                StudentId = r.GetInt64(1),
                ExamId = r.GetInt64(2),
                Subject = r.GetString(3),
                Score = r.GetDouble(4),
                FullScore = r.GetDouble(5),
                Status = (ScoreStatus)r.GetInt32(6),
            });
        }
        return list;
    }

    public void UpsertScore(SqliteConnection conn, SqliteTransaction tx, ScoreRecord s)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO scores (student_id, exam_id, subject, score, full_score, status)
            VALUES ($sid, $eid, $sub, $sc, $full, $st)
            ON CONFLICT(student_id, exam_id, subject) DO UPDATE SET
                score = excluded.score, full_score = excluded.full_score, status = excluded.status;
            """;
        cmd.Parameters.AddWithValue("$sid", s.StudentId);
        cmd.Parameters.AddWithValue("$eid", s.ExamId);
        cmd.Parameters.AddWithValue("$sub", s.Subject);
        cmd.Parameters.AddWithValue("$sc", s.Score);
        cmd.Parameters.AddWithValue("$full", s.FullScore);
        cmd.Parameters.AddWithValue("$st", (int)s.Status);
        cmd.ExecuteNonQuery();
    }

    /// <summary>批量写成绩（导入用，一个事务）。返回写入行数。</summary>
    public int BulkUpsertScores(IReadOnlyList<ScoreRecord> rows)
    {
        if (rows.Count == 0) return 0;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var row in rows) UpsertScore(conn, tx, row);
        tx.Commit();
        return rows.Count;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  年级排名（录入值）
    // ────────────────────────────────────────────────────────────────────────

    public Dictionary<long, int> GetGradeRanks(long examId)
    {
        var map = new Dictionary<long, int>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT student_id, grade_rank FROM grade_ranks WHERE exam_id = $eid;";
        cmd.Parameters.AddWithValue("$eid", examId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetInt64(0)] = r.GetInt32(1);
        return map;
    }

    public void SetGradeRank(long studentId, long examId, int gradeRank)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (gradeRank <= 0)
        {
            cmd.CommandText = "DELETE FROM grade_ranks WHERE student_id = $sid AND exam_id = $eid;";
        }
        else
        {
            cmd.CommandText = """
                INSERT INTO grade_ranks (student_id, exam_id, grade_rank) VALUES ($sid, $eid, $rank)
                ON CONFLICT(student_id, exam_id) DO UPDATE SET grade_rank = excluded.grade_rank;
                """;
            cmd.Parameters.AddWithValue("$rank", gradeRank);
        }
        cmd.Parameters.AddWithValue("$sid", studentId);
        cmd.Parameters.AddWithValue("$eid", examId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>批量写入年级排名（导入带「年级排名」列时用）。</summary>
    public void BulkSetGradeRanks(IReadOnlyDictionary<long, int> byStudentId, long examId)
    {
        if (byStudentId.Count == 0) return;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var kv in byStudentId)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO grade_ranks (student_id, exam_id, grade_rank) VALUES ($sid, $eid, $rank)
                ON CONFLICT(student_id, exam_id) DO UPDATE SET grade_rank = excluded.grade_rank;
                """;
            cmd.Parameters.AddWithValue("$sid", kv.Key);
            cmd.Parameters.AddWithValue("$eid", examId);
            cmd.Parameters.AddWithValue("$rank", kv.Value);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  小组 / 成员快照
    // ────────────────────────────────────────────────────────────────────────

    public List<StudentGroup> GetGroups()
    {
        var list = new List<StudentGroup>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, color FROM groups ORDER BY id;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new StudentGroup { Id = r.GetInt64(0), Name = r.GetString(1), Color = r.GetString(2) });
        }
        return list;
    }

    public long UpsertGroup(StudentGroup g)
    {
        using var conn = Open();
        using (var cmd = conn.CreateCommand())
        {
            if (g.Id > 0)
            {
                cmd.CommandText = "UPDATE groups SET name = $name, color = $color WHERE id = $id;";
                cmd.Parameters.AddWithValue("$id", g.Id);
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO groups (name, color) VALUES ($name, $color)
                    ON CONFLICT(name) DO UPDATE SET color = excluded.color;
                    """;
            }
            cmd.Parameters.AddWithValue("$name", g.Name);
            cmd.Parameters.AddWithValue("$color", g.Color);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            if (g.Id > 0)
            {
                cmd.CommandText = "SELECT id FROM groups WHERE id = $id;";
                cmd.Parameters.AddWithValue("$id", g.Id);
            }
            else
            {
                cmd.CommandText = "SELECT id FROM groups WHERE name = $name;";
                cmd.Parameters.AddWithValue("$name", g.Name);
            }
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }
    }

    public void DeleteGroup(long groupId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM groups WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", groupId);
        cmd.ExecuteNonQuery();
    }

    public List<GroupMember> GetGroupMembers()
    {
        var list = new List<GroupMember>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, group_id, student_id, effective_exam_id FROM group_members;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new GroupMember
            {
                Id = r.GetInt64(0),
                GroupId = r.GetInt64(1),
                StudentId = r.GetInt64(2),
                EffectiveExamId = r.GetInt64(3),
            });
        }
        return list;
    }

    /// <summary>追加一条成员快照。**换人必须走这里追加新行**，不要更新旧行 —— 那会篡改历史考试的小组构成。</summary>
    public long AddGroupMember(long groupId, long studentId, long effectiveExamId)
    {
        using var conn = Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO group_members (group_id, student_id, effective_exam_id) VALUES ($gid, $sid, $eid);";
            cmd.Parameters.AddWithValue("$gid", groupId);
            cmd.Parameters.AddWithValue("$sid", studentId);
            cmd.Parameters.AddWithValue("$eid", effectiveExamId);
            cmd.ExecuteNonQuery();
        }

        // last_insert_rowid() 是连接级的，同一连接紧接着取即可
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }
    }

    public void DeleteGroupMember(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM group_members WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  模块配置（JSON 字符串）
    // ────────────────────────────────────────────────────────────────────────

    public string? GetKv(string key)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_kv WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetKv(string key, string value)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app_kv (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  小工具
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>日期落盘统一 yyyy-MM-dd + 固定区域，避免老师机器上的区域设置把「2026/10/6」写歪。</summary>
    private static string FormatDate(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime ParseDate(string s)
        => DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2) ? d2 : DateTime.MinValue);

    /// <summary>一个事务里做整批导入（学生 + 考试 + 成绩 + 年级排名）。返回统计，供界面汇报。</summary>
    public ImportCommitResult CommitImport(ImportBatch batch)
    {
        var result = new ImportCommitResult();
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        foreach (var stu in batch.Students)
        {
            var id = UpsertStudent(conn, tx, stu);
            batch.StudentIdByNo[stu.StudentNo] = id;
            result.StudentCount++;
        }

        foreach (var exam in batch.Exams)
        {
            var id = UpsertExam(conn, tx, exam);
            batch.ExamIdByKey[ImportBatch.ExamKey(exam)] = id;
            result.ExamCount++;
        }

        // 成绩行靠 (学号, 考试键) 引用**还没落库**的自增 id —— 学生与考试此时已经 upsert 完，
        // 映射必然命中；真出现漏的（理论上不会）宁可跳过，也不要往库里写孤儿行。
        foreach (var row in batch.Scores)
        {
            if (!batch.StudentIdByNo.TryGetValue(row.StudentNo, out var sid)) continue;
            if (!batch.ExamIdByKey.TryGetValue(row.ExamKey, out var eid)) continue;
            UpsertScore(conn, tx, new ScoreRecord
            {
                StudentId = sid,
                ExamId = eid,
                Subject = row.Subject,
                Score = row.Score,
                FullScore = row.FullScore,
                Status = row.Status,
            });
            result.ScoreCount++;
        }

        foreach (var gr in batch.GradeRanks)
        {
            if (!batch.StudentIdByNo.TryGetValue(gr.StudentNo, out var sid)) continue;
            if (!batch.ExamIdByKey.TryGetValue(gr.ExamKey, out var eid)) continue;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO grade_ranks (student_id, exam_id, grade_rank) VALUES ($sid, $eid, $rank)
                ON CONFLICT(student_id, exam_id) DO UPDATE SET grade_rank = excluded.grade_rank;
                """;
            cmd.Parameters.AddWithValue("$sid", sid);
            cmd.Parameters.AddWithValue("$eid", eid);
            cmd.Parameters.AddWithValue("$rank", gr.GradeRank);
            cmd.ExecuteNonQuery();
            result.GradeRankCount++;
        }

        tx.Commit();
        return result;
    }
}

/// <summary>一批待入库的数据（导入器 → 数据库 的传输对象）。</summary>
public sealed class ImportBatch
{
    public List<Student> Students { get; } = new();
    public List<Exam> Exams { get; } = new();
    /// <summary>成绩行以 (学号, 考试键) 引用尚未落库的学生/考试，id 在 <see cref="GradeDatabase.CommitImport"/> 里解析。</summary>
    public List<PendingScore> Scores { get; } = new();
    public List<PendingGradeRank> GradeRanks { get; } = new();

    /// <summary>学号 → 学生 id（提交时装填）。</summary>
    public Dictionary<string, long> StudentIdByNo { get; } = new(StringComparer.Ordinal);
    /// <summary>考试键 → 考试 id（提交时装填）。</summary>
    public Dictionary<string, long> ExamIdByKey { get; } = new(StringComparer.Ordinal);

    /// <summary>考试的唯一键：名称 + 日期。同一天同名的视为同一场（重复导入应覆盖而不是新建）。</summary>
    public static string ExamKey(Exam e)
        => $"{e.Name}\u0001{e.ExamDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
}

public sealed class ImportCommitResult
{
    public int StudentCount { get; set; }
    public int ExamCount { get; set; }
    public int ScoreCount { get; set; }
    public int GradeRankCount { get; set; }
}

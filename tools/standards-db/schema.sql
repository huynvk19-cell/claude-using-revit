-- Standards database (SQLite). Built by standards_db.py; nothing here is edited by hand in the .db file.
--   catalogue tables  <- the drawing tables of domain/drafting-sheet-layout.md §4 and domain/drafting-sheet-set-<type>.md
--                        (the .md files stay the source: the tables below are rebuilt from them every time)
--   kind              <- domain/drafting-coverage.md §2
--   condition         <- data/condition-keys.csv, data/condition-map.csv
--   correction        <- data/corrections.csv (the log of what the user corrected; this file is its source)

CREATE TABLE stage (
    key   TEXT PRIMARY KEY,          -- YT / CS / KT / TC / HC
    rank  INTEGER NOT NULL,          -- order in time: a drawing is needed when its rank <= the job's rank
    name  TEXT NOT NULL
);
INSERT INTO stage VALUES ('YT', 0, 'Ý tưởng / phương án'), ('CS', 1, 'Thiết kế cơ sở'), ('KT', 2, 'Thiết kế kỹ thuật'),
                         ('TC', 3, 'Thiết kế bản vẽ thi công'), ('HC', 4, 'Hoàn công');

CREATE TABLE building_type (
    key           TEXT PRIMARY KEY,  -- core, townhouse, villa, highrise, public, industrial, airport
    name          TEXT NOT NULL,     -- the title of its file
    paper         TEXT,              -- paper sizes its file allows, the default first: "A2", "A1, A0"
    replaces_core INTEGER NOT NULL,  -- 1 = its table replaces the core set, 0 = its rows are added to the core set
    source_file   TEXT NOT NULL
);

CREATE TABLE kind (                  -- drawing kind = the `Loại` column; how far Claude can take it
    key            TEXT PRIMARY KEY,
    name           TEXT NOT NULL,
    skills         TEXT NOT NULL,
    workflow       TEXT NOT NULL,
    do_commands    TEXT NOT NULL,
    audit_commands TEXT NOT NULL,
    level          INTEGER NOT NULL, -- 1-4 (drafting-coverage.md §1)
    missing        TEXT NOT NULL
);

CREATE TABLE condition (             -- a fact about the building a drawing row can depend on
    key    TEXT PRIMARY KEY,         -- elevator, basement, pool...
    label  TEXT NOT NULL
);

CREATE TABLE catalog_row (           -- one drawing row of a set table
    id             INTEGER PRIMARY KEY,
    type           TEXT NOT NULL REFERENCES building_type(key),
    seq            INTEGER NOT NULL, -- order inside the type = sheet order
    section        TEXT NOT NULL,    -- heading above the table (sub-type of public buildings, project supplement...)
    grp            INTEGER,          -- sheet group 0-8 (drafting-sheet-layout.md §3)
    drawing        TEXT NOT NULL,
    scale          TEXT NOT NULL,    -- as written
    scale_coarse   INTEGER,          -- largest denominator: 1:200-1:100 -> 200
    scale_fine     INTEGER,          -- smallest denominator: 1:200-1:100 -> 100
    stage          TEXT NOT NULL REFERENCES stage(key),
    condition      TEXT NOT NULL,    -- as written, '' = always
    condition_expr TEXT NOT NULL,    -- keys: a&b = both, a|b = either, '' = always
    content        TEXT NOT NULL,    -- mandatory content
    source         TEXT NOT NULL     -- file:line
);

CREATE TABLE catalog_row_kind (
    row_id  INTEGER NOT NULL REFERENCES catalog_row(id),
    kind    TEXT NOT NULL REFERENCES kind(key),
    PRIMARY KEY (row_id, kind)
);

CREATE TABLE catalog_row_condition ( -- every key the row's condition_expr names
    row_id     INTEGER NOT NULL REFERENCES catalog_row(id),
    condition  TEXT NOT NULL REFERENCES condition(key),
    PRIMARY KEY (row_id, condition)
);

CREATE TABLE correction (            -- one thing the user corrected or decided
    id             TEXT PRIMARY KEY, -- <date>-<slug>
    date           TEXT NOT NULL,    -- YYYY-MM-DD, the day the user said it
    scope          TEXT NOT NULL,    -- all, or building type keys: "villa", "townhouse, villa"
    kind           TEXT NOT NULL,    -- drawing kind keys, '' = not about one kind
    topic          TEXT NOT NULL,    -- one or two words: paper, text, naming, sheet-list, stair-dims...
    rule           TEXT NOT NULL,    -- the correction, written as a rule
    why            TEXT NOT NULL,    -- the user's reason, '' when none was given
    status         TEXT NOT NULL CHECK (status IN ('open', 'in-repo', 'superseded')),
    repo_ref       TEXT NOT NULL,    -- where the rule lives now: file + § or rule code
    superseded_by  TEXT NOT NULL     -- id of the correction that replaced it
);

-- A set as one list: rows of the type + the core rows when the type adds to the core set.
CREATE VIEW v_catalog AS
SELECT r.id, r.type, r.seq, r.section, r.grp, r.drawing, r.scale, r.scale_coarse, r.scale_fine,
       r.stage, s.rank AS stage_rank, r.condition, r.condition_expr, r.content, r.source,
       (SELECT group_concat(k.kind, ', ') FROM catalog_row_kind k WHERE k.row_id = r.id) AS kinds,
       (SELECT min(kd.level) FROM catalog_row_kind k JOIN kind kd ON kd.key = k.kind WHERE k.row_id = r.id) AS level
FROM catalog_row r JOIN stage s ON s.key = r.stage;

CREATE VIEW v_set AS
SELECT t.key AS set_type, c.* FROM building_type t JOIN v_catalog c
    ON c.type = t.key OR (c.type = 'core' AND t.replaces_core = 0 AND t.key <> 'core');

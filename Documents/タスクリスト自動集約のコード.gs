/**
 * タスクリスト自動集約（一方向同期）
 *
 * 全体タスク … プログラマー／デザイナー／プランナー の3枚をまとめた「表示用」の表。
 *              分表を編集すると、値も書式（色・罫線・日付形式・ドロップダウン）も
 *              そのまま全体タスクに写ります。
 *              全体タスクは自動生成なので、ここで直接編集しても次の同期で分表の内容に戻ります。
 *              （全体タスク → 分表 の書き戻しは行いません）
 *
 * 並び順     … ①締切（D列）が早い順。締切を書いていない行は一番下。
 *              ②職能（プログラマー → デザイナー → プランナー）。
 *              ③担当者（G列）。順序は分表のドロップダウンの並びそのまま。
 *              ④分表での行順。
 *              分表の行順は変えません（入力中に行が飛ぶと困るため）。
 *
 * ドロップダウン … 分表3枚の名单を合わせたものが全体タスクに入ります（重複は1つに）。
 *              空行にも敷くので、新しい行でも同じ名单が出ます。
 *              分表側の名单はこのスクリプトでは一切変更しません。
 *
 * J列・K列   … 同期用の隠し列（職能＝どの分表か、元の行＝分表の行番号）。触らないでください。
 *
 * 読み書き先は getTaskBook_() で新しいタスクリストだけに制限します。
 * 参考にした古い表（ケースステディ）には一切触りません。
 */

const TASK_BOOK_ID = '1wCrejAa_tKQI3cRJtinHp_5yc_8y9UCrrhDJI6y7jtI';
const TASK_SOURCE_NAMES = ['プログラマー', 'デザイナー', 'プランナー'];
const TASK_SUMMARY_NAME = '全体タスク';
const TASK_FIRST_DATA_ROW = 2;
const TASK_LAST_DATA_ROW = 1000;
const TASK_DATA_ROW_COUNT = TASK_LAST_DATA_ROW - TASK_FIRST_DATA_ROW + 1;
const TASK_NUM_COLUMNS = 9;      // A〜I（マイルステージ〜参考資料）
const TASK_COL_ATTRIBUTE = 2;    // B タスク属性
const TASK_COL_DUE = 4;          // D 締切
const TASK_COL_STATUS = 5;       // E ステータス
const TASK_COL_NAME = 6;         // F タスク名
const TASK_COL_OWNER = 7;        // G 担当者
const TASK_COL_JOB = 10;         // J 職能（同期用）
const TASK_COL_SRCROW = 11;      // K 元の行（同期用）

const TASK_DROPDOWN_COLUMNS = [TASK_COL_ATTRIBUTE, TASK_COL_DUE, TASK_COL_STATUS, TASK_COL_OWNER];
const TASK_DROPDOWN_SPARE_ROWS = 150;   // データの後ろにこれだけの空行分ドロップダウンを敷く
const TASK_OWNER_UNKNOWN = 999999;      // 名单に無い担当者は一番後ろ
const TASK_DV_SIGNATURE_KEY = 'TASK_DV_SIGNATURE';

const TASK_SUMMARY_NOTE = 'このシートは自動生成です。' +
  '分表（プログラマー／デザイナー／プランナー）を編集すると、締切が早い順でここに反映されます。' +
  'ここで直接入力しても、次の同期で分表の内容に戻ります。タスクは分表に書いてください。';

/* ------------------------------------------------------------------ */
/* メニュー                                                             */
/* ------------------------------------------------------------------ */

function onOpen() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  if (!ss || ss.getId() !== TASK_BOOK_ID) return;
  ss.getUi().createMenu('タスク同期')
    .addItem('今すぐ同期する（締切順に並べ直す）', 'syncNowFromMenu')
    .addSeparator()
    .addItem('初期設定をやり直す', 'setupTaskListFromMenu')
    .addToUi();
}

function syncNowFromMenu() {
  const count = rebuildSummary_();
  SpreadsheetApp.getUi().alert('全体タスクを同期しました。集約したタスク：' + count + ' 件');
}

function setupTaskListFromMenu() {
  SpreadsheetApp.getUi().alert(setupTaskList());
}

/* ------------------------------------------------------------------ */
/* 初期設定                                                             */
/* ------------------------------------------------------------------ */

function setupTaskList() {
  const ss = getTaskBook_();
  const summary = ss.getSheetByName(TASK_SUMMARY_NAME);
  if (!summary) throw new Error(TASK_SUMMARY_NAME + ' というシートが見つかりません');

  // 古い集約数式（A2 の配列数式）を消して、実データで同期する方式に切り替える。
  summary.getRange(TASK_FIRST_DATA_ROW, 1, TASK_DATA_ROW_COUNT, TASK_COL_SRCROW).clearContent();
  // 前回記録したドロップダウンの署名も消して、確実に敷き直す
  ScriptProperties.deleteProperty(TASK_DV_SIGNATURE_KEY);

  summary.getRange(1, TASK_COL_JOB).setValue('職能（同期用・編集しないでください）');
  summary.getRange(1, TASK_COL_SRCROW).setValue('元の行（同期用・編集しないでください）');
  summary.getRange(1, 1).setNote(TASK_SUMMARY_NOTE);
  try { summary.hideColumns(TASK_COL_JOB, 2); } catch (error) { console.log('列の非表示化をスキップしました'); }

  applyStatusFormatting_(ss);
  applySummaryProtection_(summary);
  ensureTriggers_();

  const count = rebuildSummary_();
  return '初期設定が終わりました。\n\n集約したタスク：' + count + ' 件\n' +
    '同期の向き：分表 → 全体タスク（一方向）\n' +
    '並び順：締切 → 職能（プロ・デザ・プラ） → 担当者\n' +
    'ステータス表示：完了＝灰色／遂行中＝太字';
}

/** 全体タスクは自動生成。うっかり編集しそうになったら警告を出す（編集自体は禁止しない）。 */
function applySummaryProtection_(summary) {
  try {
    const existing = summary.getProtections(SpreadsheetApp.ProtectionType.RANGE);
    for (let i = 0; i < existing.length; i++) {
      if (String(existing[i].getDescription() || '').indexOf('自動生成') === 0) return;
    }
    const range = summary.getRange(TASK_FIRST_DATA_ROW, 1, TASK_DATA_ROW_COUNT, TASK_NUM_COLUMNS);
    const protection = range.protect();
    protection.setDescription('自動生成（分表を編集してください）');
    protection.setWarningOnly(true);
  } catch (error) {
    console.log('全体タスクの警告表示は設定できませんでした（同期には影響しません）');
  }
}

/* ------------------------------------------------------------------ */
/* トリガー                                                             */
/* ------------------------------------------------------------------ */

function ensureTriggers_() {
  const ss = getTaskBook_();
  const existing = ScriptApp.getProjectTriggers().map(function (t) { return t.getHandlerFunction(); });
  if (existing.indexOf('onTaskEdit') < 0) {
    ScriptApp.newTrigger('onTaskEdit').forSpreadsheet(ss).onEdit().create();
  }
  if (existing.indexOf('onTaskChange') < 0) {
    ScriptApp.newTrigger('onTaskChange').forSpreadsheet(ss).onChange().create();
  }
}

/** 値の編集。自分の同期処理はトリガーを起こさないので、LockService で二重実行だけ防ぐ。 */
function onTaskEdit(e) {
  if (!e || !e.range || !e.source) return;
  if (e.source.getId() !== TASK_BOOK_ID) return;
  withGuard_(function () { handleEdit_(e); });
}

/** 書式だけの変更（背景色・罫線など）は onEdit が来ないのでここで拾う。行列の挿入削除も。 */
function onTaskChange(e) {
  if (!e || !e.source) return;
  if (e.source.getId() !== TASK_BOOK_ID) return;
  const type = String(e.changeType || '');
  const rebuildTypes = ['FORMAT', 'INSERT_ROW', 'INSERT_COLUMN', 'REMOVE_ROW', 'REMOVE_COLUMN'];
  if (rebuildTypes.indexOf(type) < 0) return;

  // 同期直後のFORMATイベントで無限ループにならないようにする
  if (CacheService.getScriptCache().get('TASK_LAST_REBUILD')) return;

  withGuard_(function () { rebuildSummary_(); });
}

function withGuard_(work) {
  const lock = LockService.getScriptLock();
  if (!lock.tryLock(10000)) return;
  try {
    work();
  } catch (error) {
    console.error('同期に失敗しました: ' + (error && error.message ? error.message : error));
  } finally {
    lock.releaseLock();
  }
}

/* ------------------------------------------------------------------ */
/* 編集の振り分け                                                       */
/* ------------------------------------------------------------------ */

function handleEdit_(e) {
  const sheetName = e.range.getSheet().getName();
  const row = e.range.getRow();
  const column = e.range.getColumn();

  if (row < TASK_FIRST_DATA_ROW) return;

  if (sheetName === TASK_SUMMARY_NAME) {
    // 全体タスクは表示専用。直接編集されたら分表の内容で上書きし直す（書き戻しはしない）。
    if (column >= TASK_COL_JOB) return;
    rebuildSummary_();
    return;
  }

  if (TASK_SOURCE_NAMES.indexOf(sheetName) < 0) return;
  handleSourceEdit_(sheetName, row, column, e.range.getNumRows(), e.range.getNumColumns());
}

/** 分表 → 全体タスク */
function handleSourceEdit_(sheetName, row, column, numRows, numCols) {
  const ss = getTaskBook_();
  const summary = ss.getSheetByName(TASK_SUMMARY_NAME);
  const summaryRow = readSourceToSummary_(summary)[sheetName + ':' + row];

  // タスク名(F)・締切(D)・担当者(G)が変わった場合は並び順が変わるので作り直す
  const structural = (column === TASK_COL_NAME) || (column === TASK_COL_DUE) ||
    (column === TASK_COL_OWNER) || (numRows > 1) || (numCols > 1);

  if (!summaryRow || structural) {
    rebuildSummary_();
    return;
  }
  copySourceRowToSummary_(ss, sheetName, row, summaryRow);
}

/* ------------------------------------------------------------------ */
/* 集約の作り直し                                                       */
/* ------------------------------------------------------------------ */

function rebuildSummary_() {
  const ss = getTaskBook_();
  const summary = ss.getSheetByName(TASK_SUMMARY_NAME);
  if (!summary) throw new Error(TASK_SUMMARY_NAME + ' というシートが見つかりません');

  const merged = readMergedValidations_();
  const ownerOrder = readOwnerOrder_(merged);
  const tasks = collectTasks_(ss, ownerOrder);
  const sheetIds = getSheetIds_(ss);
  const summaryId = sheetIds[TASK_SUMMARY_NAME];

  const requests = tasks.map(function (task, index) {
    const destRow = TASK_FIRST_DATA_ROW + index;
    return {
      copyPaste: {
        source: {
          sheetId: sheetIds[task.job],
          startRowIndex: task.sourceRow - 1, endRowIndex: task.sourceRow,
          startColumnIndex: 0, endColumnIndex: TASK_NUM_COLUMNS
        },
        destination: {
          sheetId: summaryId,
          startRowIndex: destRow - 1, endRowIndex: destRow,
          startColumnIndex: 0, endColumnIndex: TASK_NUM_COLUMNS
        },
        pasteType: 'PASTE_NORMAL'
      }
    };
  });

  if (requests.length) Sheets.Spreadsheets.batchUpdate({ requests: requests }, TASK_BOOK_ID);

  // タスクが減った分、残った古い行を消す（書式とドロップダウンは残す）
  const lastNewRow = TASK_FIRST_DATA_ROW + tasks.length - 1;
  const previousLastRow = findSummaryLastRow_(summary);
  if (previousLastRow > lastNewRow) {
    summary.getRange(lastNewRow + 1, 1, previousLastRow - lastNewRow, TASK_COL_SRCROW).clearContent();
  }

  if (tasks.length) {
    summary.getRange(TASK_FIRST_DATA_ROW, TASK_COL_JOB, tasks.length, 2)
      .setValues(tasks.map(function (t) { return [t.job, t.sourceRow]; }));
  }

  // 分表3枚を合わせたドロップダウンを全体タスクに敷く（空行も含めて）
  syncDropdownsToSummary_(summaryId, merged, Math.max(lastNewRow, previousLastRow));

  CacheService.getScriptCache().put('TASK_LAST_REBUILD', String(new Date().getTime()), 10);
  SpreadsheetApp.flush();
  return tasks.length;
}

function collectTasks_(ss, ownerOrder) {
  const tasks = [];
  TASK_SOURCE_NAMES.forEach(function (name, jobIndex) {
    const sheet = ss.getSheetByName(name);
    if (!sheet) return;
    const rowCount = usedRowCount_(sheet);
    if (rowCount < 1) return;
    const values = sheet.getRange(TASK_FIRST_DATA_ROW, 1, rowCount, TASK_NUM_COLUMNS).getValues();
    values.forEach(function (row, index) {
      const taskName = row[TASK_COL_NAME - 1];
      if (taskName === '' || taskName === null || taskName === undefined) return;
      const owner = normalizeText_(row[TASK_COL_OWNER - 1]);
      tasks.push({
        job: name,
        jobOrder: jobIndex,
        sourceRow: TASK_FIRST_DATA_ROW + index,
        due: dueKey_(row[TASK_COL_DUE - 1]),
        ownerOrder: (ownerOrder[owner] === undefined) ? TASK_OWNER_UNKNOWN : ownerOrder[owner]
      });
    });
  });

  // ①締切が早い順（未記入は一番下）②職能 ③担当者 ④分表での行順
  tasks.sort(function (a, b) {
    if (a.due === null && b.due === null) return compareJob_(a, b);
    if (a.due === null) return 1;
    if (b.due === null) return -1;
    if (a.due !== b.due) return a.due - b.due;
    return compareJob_(a, b);
  });
  return tasks;
}

function compareJob_(a, b) {
  if (a.jobOrder !== b.jobOrder) return a.jobOrder - b.jobOrder;
  if (a.ownerOrder !== b.ownerOrder) return a.ownerOrder - b.ownerOrder;
  return a.sourceRow - b.sourceRow;
}

function dueKey_(value) {
  if (value instanceof Date && !isNaN(value.getTime())) return value.getTime();
  if (typeof value === 'number' && isFinite(value)) return value;
  return null;
}

function normalizeText_(value) {
  if (value === null || value === undefined) return '';
  return String(value).trim();
}

/** 実際に何か書いてある行まで（毎回999行読むと遅いため） */
function usedRowCount_(sheet) {
  const lastRow = Math.min(sheet.getLastRow(), TASK_LAST_DATA_ROW);
  if (lastRow < TASK_FIRST_DATA_ROW) return 0;
  return lastRow - TASK_FIRST_DATA_ROW + 1;
}

function copySourceRowToSummary_(ss, sheetName, sourceRow, summaryRow) {
  const sheetIds = getSheetIds_(ss);
  Sheets.Spreadsheets.batchUpdate({
    requests: [{
      copyPaste: {
        source: { sheetId: sheetIds[sheetName], startRowIndex: sourceRow - 1, endRowIndex: sourceRow, startColumnIndex: 0, endColumnIndex: TASK_NUM_COLUMNS },
        destination: { sheetId: sheetIds[TASK_SUMMARY_NAME], startRowIndex: summaryRow - 1, endRowIndex: summaryRow, startColumnIndex: 0, endColumnIndex: TASK_NUM_COLUMNS },
        pasteType: 'PASTE_NORMAL'
      }
    }]
  }, TASK_BOOK_ID);
  CacheService.getScriptCache().put('TASK_LAST_REBUILD', String(new Date().getTime()), 10);
}

/* ------------------------------------------------------------------ */
/* ドロップダウン（分表3枚 → 全体タスクの合併名单）                     */
/* ------------------------------------------------------------------ */

/**
 * 分表3枚の2行目を読んで、B・D・E・G列の入力規則をまとめる。
 * SpreadsheetApp の DataValidation はこの環境で getCriteria() が使えないため、
 * Sheets API の condition をそのまま使う。
 */
function readMergedValidations_() {
  const merged = {};
  try {
    const ranges = TASK_SOURCE_NAMES.map(function (name) {
      return "'" + name + "'!A" + TASK_FIRST_DATA_ROW + ":K" + TASK_FIRST_DATA_ROW;
    });
    const response = Sheets.Spreadsheets.get(TASK_BOOK_ID, {
      ranges: ranges,
      fields: 'sheets(properties(title),data(startColumn,rowData(values(dataValidation))))'
    });

    (response.sheets || []).forEach(function (sheet) {
      const grid = (sheet.data || [])[0];
      if (!grid) return;
      const startColumn = grid.startColumn || 0;
      const cells = ((grid.rowData || [])[0] || {}).values || [];
      cells.forEach(function (cell, offset) {
        const column = startColumn + offset + 1;
        if (TASK_DROPDOWN_COLUMNS.indexOf(column) < 0) return;
        const validation = cell && cell.dataValidation;
        if (!validation || !validation.condition) return;

        if (!merged[column]) {
          merged[column] = {
            condition: { type: validation.condition.type, values: [] },
            strict: (validation.strict === undefined) ? false : validation.strict,
            showCustomUi: (validation.showCustomUi === undefined) ? true : validation.showCustomUi,
            inputMessage: validation.inputMessage || ''
          };
        }
        (validation.condition.values || []).forEach(function (item) {
          const text = String(item.userEnteredValue === undefined || item.userEnteredValue === null ? '' : item.userEnteredValue);
          if (merged[column].condition.values.indexOf(text) < 0) merged[column].condition.values.push(text);
        });
      });
    });
  } catch (error) {
    console.log('入力規則の読み取りに失敗しました: ' + (error && error.message ? error.message : error));
  }
  return merged;
}

/** 担当者ドロップダウンの並び → 名前ごとの順位（並び替えに使う） */
function readOwnerOrder_(merged) {
  const order = {};
  const info = merged[TASK_COL_OWNER];
  if (!info) return order;
  let index = 0;
  info.condition.values.forEach(function (name) {
    const key = normalizeText_(name);
    if (key === '' || order[key] !== undefined) return;
    order[key] = index++;
  });
  return order;
}

/**
 * 全体タスクの B・D・E・G 列に、分表を合わせた入力規則を敷く。
 * データ行だけでなく後ろの空行にも敷くので、新しい行でも同じ名单が出る。
 * 中身が前回と同じなら何もしない（毎回書き換えると遅くなるため）。
 */
function syncDropdownsToSummary_(summaryId, merged, lastRow) {
  try {
    const lastTargetRow = Math.min(lastRow + TASK_DROPDOWN_SPARE_ROWS, TASK_LAST_DATA_ROW);
    const rowCount = lastTargetRow - TASK_FIRST_DATA_ROW + 1;
    if (rowCount < 1) return;

    const signatureParts = [];
    const requests = [];
    TASK_DROPDOWN_COLUMNS.forEach(function (column) {
      const info = merged[column];
      if (!info || !info.condition.type) return;
      if (info.condition.type === 'ONE_OF_LIST' && !info.condition.values.length) return;

      signatureParts.push(column + ':' + info.condition.type + ':' + info.condition.values.join('|'));
      requests.push({
        repeatCell: {
          range: {
            sheetId: summaryId,
            startRowIndex: TASK_FIRST_DATA_ROW - 1, endRowIndex: lastTargetRow,
            startColumnIndex: column - 1, endColumnIndex: column
          },
          cell: {
            dataValidation: {
              condition: { type: info.condition.type, values: info.condition.values.map(function (text) { return { userEnteredValue: text }; }) },
              inputMessage: info.inputMessage,
              strict: info.strict,
              showCustomUi: info.showCustomUi
            }
          },
          fields: 'dataValidation'
        }
      });
    });

    const signature = signatureParts.join('#');
    if (!requests.length || signature.length === 0) {
      console.log('入力規則: 分表から読み取れなかったため、全体タスクは変更していません');
      return;
    }
    if (ScriptProperties.getProperty(TASK_DV_SIGNATURE_KEY) === signature) return;

    Sheets.Spreadsheets.batchUpdate({ requests: requests }, TASK_BOOK_ID);
    ScriptProperties.setProperty(TASK_DV_SIGNATURE_KEY, signature);
  } catch (error) {
    console.log('ドロップダウンの同期をスキップしました: ' + (error && error.message ? error.message : error));
  }
}

/* ------------------------------------------------------------------ */
/* 読み取り補助                                                         */
/* ------------------------------------------------------------------ */

function getTaskBook_() {
  const ss = SpreadsheetApp.openById(TASK_BOOK_ID);
  if (ss.getId() !== TASK_BOOK_ID) throw new Error('対象のスプレッドシートではありません');
  return ss;
}

function getSheetIds_(ss) {
  const map = {};
  ss.getSheets().forEach(function (sheet) { map[sheet.getName()] = sheet.getSheetId(); });
  return map;
}

/** 全体タスクの行番号 → { job, sourceRow } */
function readSummaryToSource_(summary) {
  const map = {};
  const rowCount = usedRowCount_(summary);
  if (rowCount < 1) return map;
  const values = summary.getRange(TASK_FIRST_DATA_ROW, TASK_COL_JOB, rowCount, 2).getValues();
  values.forEach(function (row, index) {
    if (!row[0] || !row[1]) return;
    map[TASK_FIRST_DATA_ROW + index] = { job: String(row[0]), sourceRow: Number(row[1]) };
  });
  return map;
}

/** 'job:sourceRow' → 全体タスクの行番号 */
function readSourceToSummary_(summary) {
  const byRow = readSummaryToSource_(summary);
  const map = {};
  Object.keys(byRow).forEach(function (summaryRow) {
    const info = byRow[summaryRow];
    map[info.job + ':' + info.sourceRow] = Number(summaryRow);
  });
  return map;
}

/** 全体タスクで最後に同期データが入っている行（無ければ 1） */
function findSummaryLastRow_(summary) {
  let last = TASK_FIRST_DATA_ROW - 1;
  const rowCount = usedRowCount_(summary);
  if (rowCount < 1) return last;
  const jobs = summary.getRange(TASK_FIRST_DATA_ROW, TASK_COL_JOB, rowCount, 1).getValues();
  jobs.forEach(function (row, index) {
    if (row[0] !== '' && row[0] !== null && row[0] !== undefined) last = TASK_FIRST_DATA_ROW + index;
  });
  return last;
}

/* ------------------------------------------------------------------ */
/* ステータスの色（完了＝灰色／遂行中＝太字）                           */
/* ------------------------------------------------------------------ */

function applyStatusFormatting_(ss) {
  const names = [TASK_SUMMARY_NAME].concat(TASK_SOURCE_NAMES);
  names.forEach(function (name) {
    try {
      const sheet = ss.getSheetByName(name);
      if (!sheet) return;

      const rules = sheet.getConditionalFormatRules();
      if (rules.length >= 2) return;   // すでに入っている設定はそのまま使う

      const range = sheet.getRange(TASK_FIRST_DATA_ROW, 1, TASK_DATA_ROW_COUNT, TASK_NUM_COLUMNS);
      const next = rules.slice();
      next.push(SpreadsheetApp.newConditionalFormatRule()
        .whenFormulaMatches('=$E' + TASK_FIRST_DATA_ROW + '="完了"')
        .setForeground('#757575')
        .setBold(false)
        .setRanges([range])
        .build());
      next.push(SpreadsheetApp.newConditionalFormatRule()
        .whenFormulaMatches('=$E' + TASK_FIRST_DATA_ROW + '="遂行中"')
        .setForeground('#000000')
        .setBold(true)
        .setRanges([range])
        .build());
      sheet.setConditionalFormatRules(next);
    } catch (error) {
      console.log('「' + name + '」のステータス表示は既存の設定をそのまま使います');
    }
  });
}

/* ------------------------------------------------------------------ */
/* 状態の確認用（読み取りだけ・表には書き込まない）                     */
/* ------------------------------------------------------------------ */

function inspectTaskBook() {
  SpreadsheetApp.flush();
  const ss = getTaskBook_();
  const response = Sheets.Spreadsheets.get(ss.getId(), {
    ranges: ['全体タスク!A1:K8', '全体タスク!G100:G101', '全体タスク!G150:G151', 'プログラマー!A1:I4', 'デザイナー!A1:I4', 'プランナー!A1:I4'],
    fields: 'sheets(properties(title),conditionalFormats,data(startRow,rowData(values(userEnteredValue,dataValidation))))'
  });

  const lines = [];
  const seenList = {};
  response.sheets.forEach(function (s) {
    lines.push('=== ' + s.properties.title + ' / 条件付き書式:' + (s.conditionalFormats || []).length + ' 件');
    (s.data || []).forEach(function (grid, gridIndex) {
      (grid.rowData || []).forEach(function (r, rowIndex) {
        const absoluteRow = (grid.startRow || 0) + rowIndex + 1;
        const cells = r.values || [];
        const parts = [];
        cells.forEach(function (cell, colIndex) {
          const value = cell.userEnteredValue
            ? (cell.userEnteredValue.formulaValue || cell.userEnteredValue.stringValue || cell.userEnteredValue.numberValue)
            : '';
          if (cell.dataValidation) {
            const type = ((cell.dataValidation.condition || {}).type) || '?';
            const options = ((cell.dataValidation.condition || {}).values || []).map(function (x) { return x.userEnteredValue; });
            const joined = options.join('/');
            const showAll = joined !== '' && !seenList[joined];
            if (showAll) seenList[joined] = true;
            parts.push((colIndex + 1) + '列DV ' + type + '[' + options.length + ']' + (showAll ? ': ' + joined : ''));
          } else if (value !== '') {
            parts.push((colIndex + 1) + '列: ' + value);
          }
        });
        lines.push('  範囲' + (gridIndex + 1) + ' 行' + absoluteRow + ': ' + (parts.length ? parts.join(' | ') : '(空)'));
      });
    });
  });
  lines.push('=== トリガー: ' + ScriptApp.getProjectTriggers().map(function (t) { return t.getHandlerFunction(); }).join(', '));
  lines.push('=== ドロップダウン署名: ' + String(ScriptProperties.getProperty(TASK_DV_SIGNATURE_KEY) || '(なし)').slice(0, 400));

  const text = lines.join('\n');
  console.log(text);
  return text;
}

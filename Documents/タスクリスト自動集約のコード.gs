/**
 * タスクリスト自動集約（一方向同期 ＋ 職種をまたぐタスクの鏡像行）
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
 * 鏡像行     … 担当者に「チーム全員」や「デザイナ全員」、あるいは別の職種の人を書くと、
 *              その職種の分表にも同じ行が「鏡像」として入ります（締切順の位置に挿入）。
 *              鏡像は**本体の写し**なので、本体を直せば鏡像も全部直ります（改一处全都改）。
 *              鏡像行で編集してよいのは **E列（ステータス）だけ**。
 *              ステータスを変えると本体に書き戻り、他の鏡像にも伝わります。
 *              それ以外の列を鏡像で編集しても、次の同期で本体の内容に戻ります。
 *              本体の行を削除すると、鏡像も一緒に消えます。
 *              鏡像は全体タスクには入れません（本体がすでに入っているので重複になるため）。
 *              この機能は既定で **OFF** です。メニューから「予定を見る」で中身を確認してから
 *              「自動反映をONにする」を押してください。
 *
 * 隠し列     … 同期の仕組みは **Q/R の2列だけ**にまとめてある（4枚とも非表示）。
 *              全体タスク Q＝職能（どの分表か）、R＝元の行（分表の行番号）。
 *              分表     Q＝タスクID、R＝種別（本体／鏡像）。
 *              ※全体タスクの Q/R と分表の Q/R は中身が別物です。触らないでください。
 *              ※A〜I が利用者のデータ。J〜P はスクリプトが一切触らない空き領域。
 *              ※昔は J/K に置いていた。初期設定をやり直すと Q/R へ移って J/K は空く。
 *
 * 【安全のための決まり】
 *   スクリプトが分表で**削除・上書きするのは、R列に「鏡像」と書いてある行だけ**です。
 *   印の無い行はすべて利用者自身のデータとして扱い、絶対に消しません。
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
const TASK_COL_JOB = 17;         // Q 職能（全体タスクの同期用・非表示）
const TASK_COL_SRCROW = 18;      // R 元の行（全体タスクの同期用・非表示）

// 分表の同期用隠し列（全体タスクの Q/R とは中身が違うので注意）
const TASK_COL_TASKID = 17;      // Q タスクID
const TASK_COL_KIND = 18;        // R 種別（本体／鏡像）

// 同期の仕組みは Q/R の2列にまとめて置く。A〜I は利用者のもの、J〜P は何もしない。
const TASK_SYNC_FIRST_COL = 17;
const TASK_SYNC_COL_COUNT = 2;
const TASK_LEGACY_SYNC_COL = 10;  // 前は J/K だった。初期設定で Q/R に移して空ける
const TASK_KIND_SOURCE = '本体';
const TASK_KIND_MIRROR = '鏡像';

const TASK_DROPDOWN_COLUMNS = [TASK_COL_ATTRIBUTE, TASK_COL_DUE, TASK_COL_STATUS, TASK_COL_OWNER];
const TASK_DROPDOWN_SPARE_ROWS = 150;   // データの後ろにこれだけの空行分ドロップダウンを敷く
const TASK_OWNER_UNKNOWN = 999999;      // 名单に無い担当者は一番後ろ
const TASK_DV_SIGNATURE_KEY = 'TASK_DV_SIGNATURE';

// 鏡像同期のスイッチ（ScriptProperties）。'off' の間は分表に一切書き込まない。
const TASK_MIRROR_MODE_KEY = 'TASK_MIRROR_MODE';
const TASK_MIRROR_MODE_DEFAULT = 'off';
const TASK_MIRROR_INDEX_KEY = 'TASK_MIRROR_INDEX';   // タスクID → 鏡像があるシート名

const TASK_GROUP_ALL = 'チーム全員';
const TASK_ROLELESS_OWNERS = ['外部者(ゲスト)'];     // どの職種にも属さない扱いにする名前

const TASK_SUMMARY_NOTE = 'このシートは自動生成です。' +
  '分表（プログラマー／デザイナー／プランナー）を編集すると、締切が早い順でここに反映されます。' +
  'ここで直接入力しても、次の同期で分表の内容に戻ります。タスクは分表に書いてください。';

/* ------------------------------------------------------------------ */
/* メニュー                                                             */
/* ------------------------------------------------------------------ */

function onOpen() {
  // UI は SpreadsheetApp.getUi() から取る（Spreadsheet に getUi は無い）
  SpreadsheetApp.getUi()
    .createMenu('タスク同期')
    .addItem('今すぐ同期する（締切順に並べ直す）', 'syncNowFromMenu')
    .addSeparator()
    .addItem('職種をまたぐタスク：予定を見る（表は変わりません）', 'previewMirrorPlanFromMenu')
    .addItem('職種をまたぐタスク：自動反映をONにする', 'enableMirrorSyncFromMenu')
    .addItem('職種をまたぐタスク：自動反映をOFFにする', 'disableMirrorSyncFromMenu')
    .addSeparator()
    .addItem('初期設定をやり直す', 'setupTaskListFromMenu')
    .addToUi();
}

function syncNowFromMenu() {
  const count = rebuildSummary_();
  SpreadsheetApp.getUi().alert('全体タスクを同期しました。集約したタスク：' + count + ' 件');
}

function previewMirrorPlanFromMenu() {
  const text = inspectMirrors();
  SpreadsheetApp.getUi().alert('職種をまたぐタスクの予定（表は変更していません）\n\n' + text.slice(0, 3500));
}

function enableMirrorSyncFromMenu() {
  ScriptProperties.setProperty(TASK_MIRROR_MODE_KEY, 'on');
  const count = rebuildSummary_();
  SpreadsheetApp.getUi().alert('職種をまたぐタスクの自動反映をONにしました。\n\n集約したタスク：' + count + ' 件');
}

function disableMirrorSyncFromMenu() {
  ScriptProperties.setProperty(TASK_MIRROR_MODE_KEY, 'off');
  SpreadsheetApp.getUi().alert('職種をまたぐタスクの自動反映をOFFにしました。\n' +
    'すでに作った鏡像行は残ったままです。消したい場合は「今すぐ同期する」を実行してください（OFFの間は消しません）。');
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

  summary.getRange(1, 1).setNote(TASK_SUMMARY_NOTE);

  // 全体タスクは毎回作り直すので、昔 J/K に入れていた分は消して Q/R に置き直す
  clearLegacySyncColumns_(summary);
  summary.getRange(1, TASK_COL_JOB).setValue('職能（同期用・編集しないでください）');
  summary.getRange(1, TASK_COL_SRCROW).setValue('元の行（同期用・編集しないでください）');
  hideSyncColumns_(summary);

  // 分表にも同期用の隠し列（タスクID／種別）を用意する
  TASK_SOURCE_NAMES.forEach(function (name) {
    try {
      const sheet = ss.getSheetByName(name);
      if (!sheet) return;
      migrateLegacySyncColumns_(sheet);   // 昔 J/K に書いた中身を Q/R へ移す（IDを失わないため）
      sheet.getRange(1, TASK_COL_TASKID).setValue('タスクID（同期用・編集しないでください）');
      sheet.getRange(1, TASK_COL_KIND).setValue('種別（同期用・編集しないでください）');
      hideSyncColumns_(sheet);
    } catch (error) {
      console.log('「' + name + '」の同期用列の設定をスキップしました');
    }
  });

  if (!ScriptProperties.getProperty(TASK_MIRROR_MODE_KEY)) {
    ScriptProperties.setProperty(TASK_MIRROR_MODE_KEY, TASK_MIRROR_MODE_DEFAULT);
  }

  applyStatusFormatting_(ss);
  applySummaryProtection_(summary);
  ensureTriggers_();

  const count = rebuildSummary_();
  return '初期設定が終わりました。\n\n集約したタスク：' + count + ' 件\n' +
    '同期の向き：分表 → 全体タスク（一方向）\n' +
    '並び順：締切 → 職能（プロ・デザ・プラ） → 担当者\n' +
    'ステータス表示：完了＝灰色／遂行中＝太字\n' +
    '職種をまたぐタスク：' + mirrorMode_() + '（メニューからONにできます）';
}

/** 同期の仕組みを置く列（Q/R）を隠す。利用者に中身を見せないため。 */
function hideSyncColumns_(sheet) {
  try {
    sheet.hideColumns(TASK_SYNC_FIRST_COL, TASK_SYNC_COL_COUNT);
  } catch (error) {
    console.log('「' + sheet.getName() + '」の同期用列を隠せませんでした');
  }
}

/**
 * 昔 J/K に置いていた同期用列を Q/R へ移し、J/K は空けて表示に戻す。
 * タスクIDを失うと鏡像行が本体と結びつかなくなるので、消す前に必ず移す。
 */
function migrateLegacySyncColumns_(sheet) {
  if (TASK_LEGACY_SYNC_COL === TASK_SYNC_FIRST_COL) return;
  const lastRow = Math.min(sheet.getLastRow(), TASK_LAST_DATA_ROW);
  if (lastRow >= 1) {
    const old = sheet.getRange(1, TASK_LEGACY_SYNC_COL, lastRow, TASK_SYNC_COL_COUNT).getValues();
    const hasContent = old.some(function (rowValues) {
      return rowValues.some(function (value) { return normalizeText_(value) !== ''; });
    });
    if (hasContent) {
      sheet.getRange(1, TASK_SYNC_FIRST_COL, lastRow, TASK_SYNC_COL_COUNT).setValues(old);
    }
  }
  clearLegacySyncColumns_(sheet);
}

/** 昔の J/K を空にして、隠れていたら表示に戻す */
function clearLegacySyncColumns_(sheet) {
  const lastRow = Math.min(sheet.getLastRow(), TASK_LAST_DATA_ROW);
  if (lastRow >= 1) {
    sheet.getRange(1, TASK_LEGACY_SYNC_COL, lastRow, TASK_SYNC_COL_COUNT).clearContent();
  }
  try {
    sheet.showColumns(TASK_LEGACY_SYNC_COL, TASK_SYNC_COL_COUNT);
  } catch (error) { }
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
    if (column > TASK_NUM_COLUMNS) return;
    rebuildSummary_();
    return;
  }

  if (TASK_SOURCE_NAMES.indexOf(sheetName) < 0) return;
  if (column > TASK_NUM_COLUMNS) return;   // A〜I 以外（同期用の隠し列など）は触らない
  handleSourceEdit_(sheetName, row, column, e.range.getNumRows(), e.range.getNumColumns());
}

/** 分表 → 全体タスク（＋本体なら鏡像も更新） */
function handleSourceEdit_(sheetName, row, column, numRows, numCols) {
  const ss = getTaskBook_();
  const sheet = ss.getSheetByName(sheetName);
  const kind = normalizeText_(sheet.getRange(row, TASK_COL_KIND).getValue());

  // 鏡像行の編集 … ステータス(E)だけは本体に書き戻す。それ以外は本体で上書きし直す。
  if (kind === TASK_KIND_MIRROR) {
    handleMirrorEdit_(ss, sheetName, row, column);
    return;
  }

  const summary = ss.getSheetByName(TASK_SUMMARY_NAME);
  const summaryRow = readSourceToSummary_(summary)[sheetName + ':' + row];

  // タスク名(F)・締切(D)・担当者(G)が変わった場合は並び順や鏡像先が変わるので作り直す
  const structural = (column === TASK_COL_NAME) || (column === TASK_COL_DUE) ||
    (column === TASK_COL_OWNER) || (numRows > 1) || (numCols > 1);

  if (!summaryRow || structural) {
    rebuildSummary_();
    return;
  }
  copySourceRowToSummary_(ss, sheetName, row, summaryRow);
  refreshMirrorsOfSourceRow_(ss, sheetName, row);
}

function handleMirrorEdit_(ss, sheetName, row, column) {
  const sheet = ss.getSheetByName(sheetName);
  const id = normalizeText_(sheet.getRange(row, TASK_COL_TASKID).getValue());
  const source = findSourceById_(ss, id);
  if (!source) { rebuildSummary_(); return; }

  if (column === TASK_COL_STATUS) {
    const status = sheet.getRange(row, TASK_COL_STATUS).getValue();
    ss.getSheetByName(source.sheet).getRange(source.row, TASK_COL_STATUS).setValue(status);
    rebuildSummary_();
    return;
  }

  // ステータス以外を鏡像で直した場合 → 本体の内容で上書きし直す
  const sheetIds = getSheetIds_(ss);
  copyRow_(sheetIds, source.sheet, source.row, sheetName, row);
  sheet.getRange(row, TASK_COL_TASKID).setValue(source.id);
  sheet.getRange(row, TASK_COL_KIND).setValue(TASK_KIND_MIRROR);
  styleMirrorRow_(sheet, row);
  rebuildSummary_();
}

/* ------------------------------------------------------------------ */
/* 集約の作り直し                                                       */
/* ------------------------------------------------------------------ */

function rebuildSummary_() {
  const ss = getTaskBook_();
  const summary = ss.getSheetByName(TASK_SUMMARY_NAME);
  if (!summary) throw new Error(TASK_SUMMARY_NAME + ' というシートが見つかりません');

  const merged = readMergedValidations_();

  // 先に鏡像を整理する（分表に行が入るので、その後の行番号はここで確定する）
  syncMirrors_(ss, merged);
  SpreadsheetApp.flush();

  const ownerOrder = readOwnerOrder_(merged);
  const tasks = collectTasks_(ss, ownerOrder);
  const sheetIds = getSheetIds_(ss);
  const summaryId = sheetIds[TASK_SUMMARY_NAME];

  const requests = tasks.map(function (task, index) {
    const destRow = TASK_FIRST_DATA_ROW + index;
    return copyRowRequest_(sheetIds, task.job, task.sourceRow, TASK_SUMMARY_NAME, destRow);
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
    const values = sheet.getRange(TASK_FIRST_DATA_ROW, 1, rowCount, TASK_COL_KIND).getValues();
    values.forEach(function (row, index) {
      // 鏡像行は集約しない（本体がすでに入るため、そのまま入れると重複する）
      if (normalizeText_(row[TASK_COL_KIND - 1]) === TASK_KIND_MIRROR) return;
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

/* ------------------------------------------------------------------ */
/* 行のコピー（値・書式・ドロップダウンごと写す）                       */
/* ------------------------------------------------------------------ */

function copyRowRequest_(sheetIds, fromSheet, fromRow, toSheet, toRow) {
  return {
    copyPaste: {
      source: {
        sheetId: sheetIds[fromSheet],
        startRowIndex: fromRow - 1, endRowIndex: fromRow,
        startColumnIndex: 0, endColumnIndex: TASK_NUM_COLUMNS
      },
      destination: {
        sheetId: sheetIds[toSheet],
        startRowIndex: toRow - 1, endRowIndex: toRow,
        startColumnIndex: 0, endColumnIndex: TASK_NUM_COLUMNS
      },
      pasteType: 'PASTE_NORMAL'
    }
  };
}

function copyRow_(sheetIds, fromSheet, fromRow, toSheet, toRow) {
  Sheets.Spreadsheets.batchUpdate(
    { requests: [copyRowRequest_(sheetIds, fromSheet, fromRow, toSheet, toRow)] }, TASK_BOOK_ID);
}

function copySourceRowToSummary_(ss, sheetName, sourceRow, summaryRow) {
  copyRow_(getSheetIds_(ss), sheetName, sourceRow, TASK_SUMMARY_NAME, summaryRow);
  CacheService.getScriptCache().put('TASK_LAST_REBUILD', String(new Date().getTime()), 10);
}

/* ------------------------------------------------------------------ */
/* 職種をまたぐタスク（本体 → 鏡像）                                    */
/* ------------------------------------------------------------------ */

/**
 * 名簿を作る。担当者ドロップダウン（G列）の**並び順**から職種を推定する。
 *   「○○全員」が出てきたら、そこから次の「○○全員」までの名前がその職種のメンバー。
 *   「チーム全員」は3職種すべて。
 *   TASK_ROLELESS_OWNERS に挙げた名前はどの職種にも入れない。
 * ドロップダウンの並びを変えると名簿も変わるので、名单はスクリプトでは触らない。
 */
function buildRoleMap_(merged) {
  const info = merged ? merged[TASK_COL_OWNER] : null;
  const list = (info && info.condition && info.condition.values) ? info.condition.values : [];
  const personToRole = {};
  const groupToRole = {};
  let current = null;

  list.forEach(function (raw) {
    const token = normalizeText_(raw);
    if (!token) return;
    if (token === TASK_GROUP_ALL) {
      groupToRole[token] = TASK_SOURCE_NAMES.slice();
      current = null;
      return;
    }
    const role = roleFromGroupToken_(token);
    if (role) {
      groupToRole[token] = [role];
      current = role;
      return;
    }
    if (TASK_ROLELESS_OWNERS.indexOf(token) >= 0) {
      current = null;
      return;
    }
    if (current) personToRole[token] = current;
  });
  return { personToRole: personToRole, groupToRole: groupToRole };
}

/** 「デザイナ全員」→「デザイナー」のように、先頭の一致でシート名を探す */
function roleFromGroupToken_(token) {
  if (token.length <= 2 || token.slice(-2) !== '全員') return null;
  const stem = token.slice(0, -2);
  for (let i = 0; i < TASK_SOURCE_NAMES.length; i++) {
    if (TASK_SOURCE_NAMES[i].indexOf(stem) === 0) return TASK_SOURCE_NAMES[i];
  }
  return null;
}

/** 「呉, デザイナ全員」のような書き方に対応（カンマ・読点で区切る） */
function rolesOfOwnerText_(ownerText, roleMap) {
  const roles = [];
  String(ownerText || '').split(/[,、]/).forEach(function (raw) {
    const token = normalizeText_(raw);
    if (!token) return;
    const list = roleMap.groupToRole[token] ||
      (roleMap.personToRole[token] ? [roleMap.personToRole[token]] : null);
    if (!list) return;
    list.forEach(function (role) { if (roles.indexOf(role) < 0) roles.push(role); });
  });
  return roles;
}

/** 分表3枚を1回で読んで、本体行と鏡像行に分ける */
function readSourceSnapshot_(ss) {
  const sources = [];
  const mirrors = [];
  TASK_SOURCE_NAMES.forEach(function (name) {
    const sheet = ss.getSheetByName(name);
    if (!sheet) return;
    const rowCount = usedRowCount_(sheet);
    if (rowCount < 1) return;
    const values = sheet.getRange(TASK_FIRST_DATA_ROW, 1, rowCount, TASK_COL_KIND).getValues();
    values.forEach(function (row, index) {
      const rowIndex = TASK_FIRST_DATA_ROW + index;
      const kind = normalizeText_(row[TASK_COL_KIND - 1]);
      const id = normalizeText_(row[TASK_COL_TASKID - 1]);
      if (kind === TASK_KIND_MIRROR) {
        mirrors.push({ id: id || null, sheet: name, row: rowIndex });
        return;
      }
      const taskName = row[TASK_COL_NAME - 1];
      if (taskName === '' || taskName === null || taskName === undefined) return;
      sources.push({
        id: id || null,
        sheet: name,
        row: rowIndex,
        taskName: normalizeText_(taskName),
        owner: normalizeText_(row[TASK_COL_OWNER - 1]),
        due: dueKey_(row[TASK_COL_DUE - 1]),
        targets: []
      });
    });
  });
  return { sources: sources, mirrors: mirrors };
}

/**
 * 「何をするか」だけを決める（この関数は表に一切書き込まない）。
 * 削除対象になるのは R列＝鏡像 の行だけ。本体行は絶対に消さない。
 */
function planMirrors_(snapshot, roleMap) {
  const assigns = [];   // IDを振る本体行
  const deletes = [];   // 消す鏡像行
  const creates = [];   // 新しく作る鏡像
  const updates = [];   // 中身を本体で上書きする鏡像

  const liveIds = {};
  snapshot.sources.forEach(function (src) {
    if (!src.id || liveIds[src.id]) {
      src.id = newTaskId_();
      assigns.push({ sheet: src.sheet, row: src.row, id: src.id });
    }
    liveIds[src.id] = src;
    // 自分のシート以外が鏡像先
    src.targets = rolesOfOwnerText_(src.owner, roleMap).filter(function (role) { return role !== src.sheet; });
  });

  const mirrorsById = {};
  snapshot.mirrors.forEach(function (mirror) {
    const src = mirror.id ? liveIds[mirror.id] : null;
    // 本体が消えた、または担当者が変わってこの職種が対象でなくなった
    if (!src || src.targets.indexOf(mirror.sheet) < 0) { deletes.push(mirror); return; }
    const list = mirrorsById[mirror.id] || (mirrorsById[mirror.id] = []);
    if (list.some(function (m) { return m.sheet === mirror.sheet; })) { deletes.push(mirror); return; }
    list.push(mirror);
    updates.push({ id: mirror.id, sheet: mirror.sheet });
  });

  snapshot.sources.forEach(function (src) {
    src.targets.forEach(function (role) {
      const have = (mirrorsById[src.id] || []).some(function (m) { return m.sheet === role; });
      if (!have) {
        creates.push({ id: src.id, sheet: role, due: src.due, taskName: src.taskName, owner: src.owner });
      }
    });
  });

  return { assigns: assigns, deletes: deletes, creates: creates, updates: updates };
}

function describePlan_(plan, roleMap, mode) {
  const lines = [];
  lines.push('鏡像同期: ' + mode);
  const groups = Object.keys(roleMap.groupToRole);
  const persons = Object.keys(roleMap.personToRole);
  lines.push('名簿（担当者 → 職種）: グループ' + groups.length + '件 / 個人' + persons.length + '人');
  groups.forEach(function (g) { lines.push('  [組] ' + g + ' → ' + roleMap.groupToRole[g].join('・')); });
  lines.push('  [個人] ' + persons.map(function (p) { return p + '=' + roleMap.personToRole[p]; }).join(', '));
  lines.push('IDを振る本体行: ' + plan.assigns.length + ' 件');
  plan.assigns.slice(0, 40).forEach(function (a) { lines.push('  ' + a.sheet + ' 行' + a.row); });
  lines.push('新しく作る鏡像: ' + plan.creates.length + ' 件');
  plan.creates.slice(0, 40).forEach(function (c) {
    lines.push('  ' + c.sheet + ' ← 「' + c.taskName + '」 担当者=' + c.owner);
  });
  lines.push('中身を本体で上書きする鏡像: ' + plan.updates.length + ' 件');
  lines.push('削除する鏡像: ' + plan.deletes.length + ' 件');
  plan.deletes.slice(0, 40).forEach(function (d) {
    lines.push('  ' + d.sheet + ' 行' + d.row + '（ID=' + (d.id || 'なし') + '）');
  });
  return lines;
}

function executeMirrorPlan_(ss, plan) {
  const sheetIds = getSheetIds_(ss);

  // 1) 本体行にIDを書く（行の増減より先にやる。まだ行番号がずれていないので）
  plan.assigns.forEach(function (a) {
    const sheet = ss.getSheetByName(a.sheet);
    if (!sheet) return;
    sheet.getRange(a.row, TASK_COL_TASKID).setValue(a.id);
    sheet.getRange(a.row, TASK_COL_KIND).setValue(TASK_KIND_SOURCE);
  });

  // 2) 古くなった鏡像を消す（下の行から消さないと行番号がずれる）
  const perSheet = {};
  plan.deletes.forEach(function (d) { perSheet[d.sheet] = (perSheet[d.sheet] || []).concat([d.row]); });
  Object.keys(perSheet).forEach(function (name) {
    const sheet = ss.getSheetByName(name);
    if (!sheet) return;
    perSheet[name].sort(function (a, b) { return b - a; }).forEach(function (r) { sheet.deleteRow(r); });
  });
  SpreadsheetApp.flush();

  // 3) 行番号がずれたので読み直す
  const fresh = readSourceSnapshot_(ss);
  const sourceById = {};
  fresh.sources.forEach(function (s) { if (s.id) sourceById[s.id] = s; });

  // 4) 残す鏡像の中身を本体で上書き
  const mirrorRowOf = {};
  fresh.mirrors.forEach(function (m) { if (m.id) mirrorRowOf[m.id + '!' + m.sheet] = m.row; });
  const updateRequests = [];
  const updatedRows = [];
  plan.updates.forEach(function (u) {
    const src = sourceById[u.id];
    const row = mirrorRowOf[u.id + '!' + u.sheet];
    if (!src || !row) return;
    updateRequests.push(copyRowRequest_(sheetIds, src.sheet, src.row, u.sheet, row));
    updatedRows.push({ sheet: u.sheet, row: row });
  });
  if (updateRequests.length) Sheets.Spreadsheets.batchUpdate({ requests: updateRequests }, TASK_BOOK_ID);
  updatedRows.forEach(function (u) { styleMirrorRow_(ss.getSheetByName(u.sheet), u.row); });

  // 5) 足りない鏡像を作る（締切が早い順に、1枚ずつ位置を計算し直す）
  const sorted = plan.creates.slice().sort(function (a, b) {
    if (a.due === null && b.due === null) return 0;
    if (a.due === null) return 1;
    if (b.due === null) return -1;
    return a.due - b.due;
  });
  sorted.forEach(function (c) {
    const src = sourceById[c.id];
    const sheet = ss.getSheetByName(c.sheet);
    if (!src || !sheet) return;
    const lastRow = sheet.getLastRow();
    const position = mirrorInsertRow_(sheet, c.due);
    let newRow;
    if (position > lastRow) { sheet.insertRowAfter(lastRow); newRow = lastRow + 1; }
    else { sheet.insertRowBefore(position); newRow = position; }
    copyRow_(getSheetIds_(ss), src.sheet, src.row, c.sheet, newRow);
    sheet.getRange(newRow, TASK_COL_TASKID).setValue(c.id);
    sheet.getRange(newRow, TASK_COL_KIND).setValue(TASK_KIND_MIRROR);
    styleMirrorRow_(sheet, newRow);
  });

  writeMirrorIndex_(ss);
}

/** 締切が自分より遅い最初の行の手前（締切なしなら一番下） */
function mirrorInsertRow_(sheet, due) {
  const rowCount = usedRowCount_(sheet);
  if (rowCount < 1) return TASK_FIRST_DATA_ROW;
  if (due === null) return TASK_FIRST_DATA_ROW + rowCount;
  const dues = sheet.getRange(TASK_FIRST_DATA_ROW, TASK_COL_DUE, rowCount, 1).getValues();
  for (let i = 0; i < dues.length; i++) {
    const d = dueKey_(dues[i][0]);
    if (d === null) return TASK_FIRST_DATA_ROW + i;
    if (d > due) return TASK_FIRST_DATA_ROW + i;
  }
  return TASK_FIRST_DATA_ROW + rowCount;
}

/** 鏡像は斜体にして、本体と見た目で区別できるようにする */
function styleMirrorRow_(sheet, row) {
  try { sheet.getRange(row, 1, 1, TASK_NUM_COLUMNS).setFontStyle('italic'); } catch (error) { }
}

function newTaskId_() {
  return 'T' + new Date().getTime().toString(36) +
    Math.floor(Math.random() * 46656).toString(36) +
    Math.floor(Math.random() * 46656).toString(36);
}

function readMirrorIndex_() {
  try { return JSON.parse(ScriptProperties.getProperty(TASK_MIRROR_INDEX_KEY) || '{}'); }
  catch (error) { return {}; }
}

/** タスクID → 鏡像があるシート名。本体を軽く直したときに探す用。 */
function writeMirrorIndex_(ss) {
  const snap = readSourceSnapshot_(ss);
  const index = {};
  snap.mirrors.forEach(function (m) {
    if (!m.id) return;
    if (!index[m.id]) index[m.id] = [];
    if (index[m.id].indexOf(m.sheet) < 0) index[m.id].push(m.sheet);
  });
  ScriptProperties.setProperty(TASK_MIRROR_INDEX_KEY, JSON.stringify(index));
}

function findSourceById_(ss, id) {
  if (!id) return null;
  const snap = readSourceSnapshot_(ss);
  for (let i = 0; i < snap.sources.length; i++) {
    if (snap.sources[i].id === id) return snap.sources[i];
  }
  return null;
}

/** 本体を軽く直したとき、その鏡像だけを上書きする（全体を作り直すより速い） */
function refreshMirrorsOfSourceRow_(ss, sheetName, row) {
  if (mirrorMode_() !== 'on') return;
  const sheet = ss.getSheetByName(sheetName);
  const id = normalizeText_(sheet.getRange(row, TASK_COL_TASKID).getValue());
  if (!id) return;
  const targets = readMirrorIndex_()[id];
  if (!targets || !targets.length) return;

  const sheetIds = getSheetIds_(ss);
  const requests = [];
  const rows = [];
  targets.forEach(function (name) {
    const target = ss.getSheetByName(name);
    if (!target) return;
    const rowCount = usedRowCount_(target);
    if (rowCount < 1) return;
    const ids = target.getRange(TASK_FIRST_DATA_ROW, TASK_COL_TASKID, rowCount, 1).getValues();
    ids.forEach(function (r, i) {
      if (normalizeText_(r[0]) !== id) return;
      const targetRow = TASK_FIRST_DATA_ROW + i;
      requests.push(copyRowRequest_(sheetIds, sheetName, row, name, targetRow));
      rows.push({ sheet: target, row: targetRow });
    });
  });
  if (!requests.length) return;
  Sheets.Spreadsheets.batchUpdate({ requests: requests }, TASK_BOOK_ID);
  rows.forEach(function (r) { styleMirrorRow_(r.sheet, r.row); });
}

function mirrorMode_() {
  const mode = ScriptProperties.getProperty(TASK_MIRROR_MODE_KEY);
  return (mode === 'on' || mode === 'off') ? mode : TASK_MIRROR_MODE_DEFAULT;
}

/** OFF の間は完全に何もしない（分表への書き込みゼロ） */
function syncMirrors_(ss, merged) {
  if (mirrorMode_() !== 'on') return;
  const roleMap = buildRoleMap_(merged);
  const plan = planMirrors_(readSourceSnapshot_(ss), roleMap);
  const changeCount = plan.assigns.length + plan.deletes.length + plan.creates.length;
  if (!changeCount && !plan.updates.length) return;
  executeMirrorPlan_(ss, plan);
  if (changeCount) {
    console.log('鏡像同期: ID付与' + plan.assigns.length + ' / 新規' + plan.creates.length +
      ' / 削除' + plan.deletes.length + ' / 更新' + plan.updates.length);
  }
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
  if (!info || !info.condition || !info.condition.values) return order;
  info.condition.values.forEach(function (name, index) {
    const key = normalizeText_(name);
    if (key && order[key] === undefined) order[key] = index;
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
    ranges: ['全体タスク!A1:R8', '全体タスク!G100:G101', '全体タスク!G150:G151', 'プログラマー!A1:R4', 'デザイナー!A1:R4', 'プランナー!A1:R4'],
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
  lines.push('=== 鏡像同期: ' + mirrorMode_());

  const text = lines.join('\n');
  console.log(text);
  return text;
}

/** 鏡像同期の予定を見るだけ（表には一切書き込まない） */
function inspectMirrors() {
  const ss = getTaskBook_();
  const merged = readMergedValidations_();
  const roleMap = buildRoleMap_(merged);
  const snapshot = readSourceSnapshot_(ss);
  const plan = planMirrors_(snapshot, roleMap);

  const lines = describePlan_(plan, roleMap, mirrorMode_());
  lines.push('--- 今の分表 ---');
  lines.push('本体: ' + snapshot.sources.length + ' 行 / 鏡像: ' + snapshot.mirrors.length + ' 行');
  snapshot.sources.forEach(function (s) {
    lines.push('  [本体] ' + s.sheet + ' 行' + s.row + ' 「' + s.taskName + '」 担当者=' + s.owner +
      ' → 鏡像先: ' + (s.targets.length ? s.targets.join('・') : 'なし'));
  });
  snapshot.mirrors.forEach(function (m) {
    lines.push('  [鏡像] ' + m.sheet + ' 行' + m.row + ' ID=' + (m.id || 'なし'));
  });

  const text = lines.join('\n');
  console.log(text);
  return text;
}

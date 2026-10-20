import { spawnSync } from 'node:child_process'
import fs from 'node:fs'
import path from 'node:path'

const EXIT_CLEAN = 0
const EXIT_FOUND = 1
const EXIT_ERROR = 2

const PROGRAM = 'check-no-comments'

const USAGE = 'usage: node infrastructure/scripts/check-no-comments.mjs [--staged] [path ...]'

const STAGED_FLAG = '--staged'

const END_OF_OPTIONS = '--'

const GIT_MAX_BUFFER = 1073741824

const BLOB_BATCH = 64

const GITLINK_MODE = '160000'

const SYMLINK_MODE = '120000'

const GIT_ENVIRONMENT = Object.freeze({ GIT_OPTIONAL_LOCKS: '0', GIT_NO_REPLACE_OBJECTS: '1' })

const TOP_LEVEL_ARGUMENTS = Object.freeze(['rev-parse', '--show-toplevel'])

const LIST_ARGUMENTS = Object.freeze([
  'ls-files',
  '-z',
  '--cached',
  '--others',
  '--exclude-standard',
])

const DIFF_ARGUMENTS = Object.freeze([
  'diff',
  '--cached',
  '--raw',
  '-z',
  '--no-renames',
  '--no-abbrev',
  '--diff-filter=ACMRT',
])

const BLOB_ARGUMENTS = Object.freeze(['cat-file', '--batch'])

const LINE_COMMENT = 'line-comment'
const BLOCK_COMMENT = 'block-comment'
const DOC_COMMENT = 'doc-comment'
const MARKUP_COMMENT = 'markup-comment'
const REGION_DIRECTIVE = 'region-directive'
const HASHBANG = 'hashbang'
const UNCLASSIFIED_FILE_TYPE = 'unclassified-file-type'

const SKIP = 'skip'

const UNCLASSIFIED = 'unclassified'

const BINARY_EXTENSIONS = new Set(['.woff2', '.ttf', '.png', '.jpg', '.ico'])

const LICENSE_NAMES = new Set(['LICENSE', 'LICENSE.md', 'LICENSE.txt', 'OFL.txt'])

const NAME_FAMILIES = new Map([
  ['.env.example', 'hash'],
  ['nginx.conf', 'hash'],
  ['dockerfile', 'hash'],
  ['.dockerignore', 'hash'],
  ['.gitignore', 'hashLineStart'],
  ['.gitattributes', 'hashAfterBlanks'],
  ['.nvmrc', 'hashLineStart'],
  ['.editorconfig', 'hashOrSemicolon'],
  ['.npmrc', 'hashOrSemicolon'],
])

const EXTENSION_FAMILIES = new Map([
  ['.cs', 'csharp'],
  ['.ts', 'script'],
  ['.mts', 'script'],
  ['.cts', 'script'],
  ['.js', 'script'],
  ['.mjs', 'script'],
  ['.cjs', 'script'],
  ['.vue', 'vue'],
  ['.css', 'css'],
  ['.json', 'json'],
  ['.xml', 'markup'],
  ['.xaml', 'markup'],
  ['.csproj', 'markup'],
  ['.props', 'markup'],
  ['.targets', 'markup'],
  ['.slnx', 'markup'],
  ['.config', 'markup'],
  ['.manifest', 'markup'],
  ['.appxmanifest', 'markup'],
  ['.svg', 'markup'],
  ['.html', 'markup'],
  ['.md', 'markdown'],
  ['.sql', 'sql'],
  ['.yml', 'hash'],
  ['.yaml', 'hash'],
  ['.dockerignore', 'hash'],
])

const TAB = 9
const LF = 10
const VT = 11
const FF = 12
const CR = 13
const SPACE = 32
const QUOTE = 34
const HASH = 35
const DOLLAR = 36
const APOSTROPHE = 39
const STAR = 42
const PLUS = 43
const MINUS = 45
const DOT = 46
const SLASH = 47
const SEMICOLON = 59
const GREATER = 62
const AT = 64
const UPPER_E = 69
const OPEN_BRACKET = 91
const BACKSLASH = 92
const CLOSE_BRACKET = 93
const UNDERSCORE = 95
const BACKTICK = 96
const LOWER_E = 101
const OPEN_BRACE = 123
const CLOSE_BRACE = 125
const BOM = 0xfeff

const WHITESPACE = new RegExp('\\s')

const LETTER_OR_DIGIT = new RegExp('[\\p{L}\\p{N}]', 'u')

const REGEX_PUNCTUATORS = new Set([
  '(',
  ',',
  '=',
  ':',
  '[',
  '!',
  '&',
  '|',
  '?',
  '{',
  ';',
  '+',
  '-',
  '*',
  '%',
  '<',
  '>',
  '~',
  '^',
])

const REGEX_KEYWORDS = new Set([
  'return',
  'typeof',
  'instanceof',
  'in',
  'of',
  'new',
  'delete',
  'void',
  'throw',
  'case',
  'do',
  'else',
  'yield',
  'await',
])

const VUE_BLOCKS = Object.freeze([
  Object.freeze({
    script: true,
    open: new RegExp('<script(?=[\\s>/])', 'iy'),
    close: new RegExp('</script(?=[\\s>])', 'gi'),
  }),
  Object.freeze({
    script: false,
    open: new RegExp('<style(?=[\\s>/])', 'iy'),
    close: new RegExp('</style(?=[\\s>])', 'gi'),
  }),
])

class UsageError extends Error {}

class ScanError extends Error {}

function classify(name) {
  const base = name.toLowerCase()
  const dot = base.lastIndexOf('.')
  const extension = dot > 0 ? base.slice(dot) : ''
  if (base === '.gitkeep' || LICENSE_NAMES.has(name)) {
    return SKIP
  }
  if (BINARY_EXTENSIONS.has(extension)) {
    return SKIP
  }
  return NAME_FAMILIES.get(base) ?? EXTENSION_FAMILIES.get(extension) ?? UNCLASSIFIED
}

function isBreak(code) {
  return code === LF || code === CR
}

function isBlank(code) {
  return code === SPACE || code === TAB || code === VT || code === FF
}

function isDigit(code) {
  return code >= 48 && code <= 57
}

function isWordCode(code) {
  return (
    (code >= 65 && code <= 90) ||
    (code >= 97 && code <= 122) ||
    isDigit(code) ||
    code === UNDERSCORE
  )
}

function isScriptSpace(text, index) {
  const code = text.charCodeAt(index)
  if (code <= SPACE) {
    return code === SPACE || (code >= TAB && code <= CR)
  }
  return code > 127 && WHITESPACE.test(text[index])
}

function isScriptWord(text, index) {
  const code = text.charCodeAt(index)
  if (isWordCode(code) || code === DOLLAR || code === BACKSLASH) {
    return true
  }
  return code > 127 && !WHITESPACE.test(text[index])
}

function isSqlWordCode(code) {
  return isWordCode(code) || code === DOLLAR || code > 127
}

function lineEnd(text, from, end) {
  let j = from
  while (j < end && !isBreak(text.charCodeAt(j))) {
    j += 1
  }
  return j
}

function skipPast(text, token, from, end) {
  const at = text.indexOf(token, from)
  return at < 0 || at + token.length > end ? end : at + token.length
}

function escapeEnd(text, index, end) {
  if (text.charCodeAt(index + 1) === CR && text.charCodeAt(index + 2) === LF) {
    return Math.min(index + 3, end)
  }
  return Math.min(index + 2, end)
}

function quotedEnd(text, index, end, quote) {
  let j = index + 1
  while (j < end) {
    const code = text.charCodeAt(j)
    if (code === BACKSLASH) {
      j = escapeEnd(text, j, end)
    } else if (code === quote) {
      return j + 1
    } else if (isBreak(code)) {
      return j
    } else {
      j += 1
    }
  }
  return end
}

function slashSlashKind(text, index) {
  const triple = text.charCodeAt(index + 2) === SLASH && text.charCodeAt(index + 3) !== SLASH
  return triple ? DOC_COMMENT : LINE_COMMENT
}

function slashStarKind(text, index) {
  const double = text.charCodeAt(index + 2) === STAR && text.charCodeAt(index + 3) !== SLASH
  return double ? DOC_COMMENT : BLOCK_COMMENT
}

function eachLine(text, visit) {
  const end = text.length
  let start = 0
  while (start < end) {
    let stop = start
    while (stop < end && !isBreak(text.charCodeAt(stop))) {
      stop += 1
    }
    visit(start, stop)
    if (text.charCodeAt(stop) === CR && text.charCodeAt(stop + 1) === LF) {
      stop += 1
    }
    start = stop + 1
  }
}

function directiveName(text, from, end) {
  let start = from
  while (start < end && isBlank(text.charCodeAt(start))) {
    start += 1
  }
  let stop = start
  while (stop < end && isWordCode(text.charCodeAt(stop))) {
    stop += 1
  }
  return text.slice(start, stop)
}

function csharpString(text, from) {
  let j = from
  let dollars = 0
  let verbatim = false
  while (text.charCodeAt(j) === DOLLAR) {
    dollars += 1
    j += 1
  }
  if (text.charCodeAt(j) === AT) {
    verbatim = true
    j += 1
    while (text.charCodeAt(j) === DOLLAR) {
      dollars += 1
      j += 1
    }
  }
  if (text.charCodeAt(j) !== QUOTE) {
    return { form: 'none', next: j }
  }
  if (verbatim) {
    return { form: dollars > 0 ? 'interpolated' : 'verbatim', verbatim, body: j + 1 }
  }
  let quotes = 0
  while (text.charCodeAt(j + quotes) === QUOTE) {
    quotes += 1
  }
  if (quotes >= 3) {
    return { form: 'raw', quotes, body: j + quotes }
  }
  return { form: dollars > 0 ? 'interpolated' : 'regular', verbatim, body: j + 1 }
}

function verbatimEnd(text, from, end) {
  let j = from
  while (j < end) {
    if (text.charCodeAt(j) !== QUOTE) {
      j += 1
    } else if (text.charCodeAt(j + 1) === QUOTE) {
      j += 2
    } else {
      return j + 1
    }
  }
  return end
}

function scanCsharp(text, found) {
  const end = text.length
  const holes = []
  let i = 0
  let lineStart = true
  let inString = false
  let verbatim = false
  while (i < end) {
    const code = text.charCodeAt(i)
    if (inString) {
      if (code === QUOTE) {
        if (verbatim && text.charCodeAt(i + 1) === QUOTE) {
          i += 2
        } else {
          inString = false
          i += 1
        }
      } else if (code === BACKSLASH && !verbatim) {
        i = escapeEnd(text, i, end)
      } else if (isBreak(code) && !verbatim) {
        inString = false
      } else if (code === OPEN_BRACE) {
        if (text.charCodeAt(i + 1) === OPEN_BRACE) {
          i += 2
        } else {
          holes.push({ verbatim, depth: 0 })
          inString = false
          i += 1
        }
      } else {
        i += 1
      }
      continue
    }
    if (isBreak(code)) {
      lineStart = true
      i += 1
      continue
    }
    if (isBlank(code)) {
      i += 1
      continue
    }
    if (code === HASH && lineStart) {
      lineStart = false
      const name = directiveName(text, i + 1, end)
      if (name === 'region' || name === 'endregion') {
        found.push({ offset: i, kind: REGION_DIRECTIVE })
        i = lineEnd(text, i, end)
      } else {
        i += 1
      }
      continue
    }
    lineStart = false
    if (code === SLASH) {
      const next = text.charCodeAt(i + 1)
      if (next === SLASH) {
        found.push({ offset: i, kind: slashSlashKind(text, i) })
        i = lineEnd(text, i, end)
      } else if (next === STAR) {
        found.push({ offset: i, kind: slashStarKind(text, i) })
        i = skipPast(text, '*/', i + 2, end)
      } else {
        i += 1
      }
      continue
    }
    if (code === QUOTE || code === AT || code === DOLLAR) {
      const literal = csharpString(text, i)
      if (literal.form === 'none') {
        i = Math.max(literal.next, i + 1)
      } else if (literal.form === 'raw') {
        i = skipPast(text, '"'.repeat(literal.quotes), literal.body, end)
      } else if (literal.form === 'verbatim') {
        i = verbatimEnd(text, literal.body, end)
      } else if (literal.form === 'regular') {
        i = quotedEnd(text, literal.body - 1, end, QUOTE)
      } else {
        inString = true
        verbatim = literal.verbatim
        i = literal.body
      }
      continue
    }
    if (code === APOSTROPHE) {
      i = quotedEnd(text, i, end, APOSTROPHE)
      continue
    }
    if (holes.length > 0) {
      const hole = holes[holes.length - 1]
      if (code === OPEN_BRACE) {
        hole.depth += 1
      } else if (code === CLOSE_BRACE && hole.depth > 0) {
        hole.depth -= 1
      } else if (code === CLOSE_BRACE) {
        holes.pop()
        inString = true
        verbatim = hole.verbatim
      }
    }
    i += 1
  }
}

function skipScriptWord(text, from, end) {
  let j = from
  while (j < end && isScriptWord(text, j)) {
    j += 1
  }
  return j
}

function skipNumber(text, from, end) {
  let j = from
  while (j < end) {
    const code = text.charCodeAt(j)
    if (!isWordCode(code) && code !== DOT) {
      break
    }
    j += 1
  }
  return j
}

function regexEnd(text, from, end) {
  let inClass = false
  let j = from
  while (j < end) {
    const code = text.charCodeAt(j)
    if (isBreak(code)) {
      return -1
    }
    if (code === BACKSLASH) {
      if (j + 1 >= end || isBreak(text.charCodeAt(j + 1))) {
        return -1
      }
      j += 2
      continue
    }
    if (inClass) {
      if (code === CLOSE_BRACKET) {
        inClass = false
      }
    } else if (code === OPEN_BRACKET) {
      inClass = true
    } else if (code === SLASH) {
      return j
    }
    j += 1
  }
  return -1
}

function scanScript(text, start, end, found, hashbangAllowed) {
  const holes = []
  let depth = 0
  let inTemplate = false
  let regexAllowed = true
  let afterDot = false
  let regexBudget = (end - start) * 4 + 65536
  let i = start
  if (hashbangAllowed && start === 0 && text.startsWith('#!')) {
    found.push({ offset: 0, kind: HASHBANG })
    i = lineEnd(text, 0, end)
  }
  while (i < end) {
    const code = text.charCodeAt(i)
    if (inTemplate) {
      if (code === BACKSLASH) {
        i = escapeEnd(text, i, end)
      } else if (code === BACKTICK) {
        inTemplate = false
        regexAllowed = false
        afterDot = false
        i += 1
      } else if (code === DOLLAR && text.charCodeAt(i + 1) === OPEN_BRACE) {
        holes.push(depth)
        depth = 0
        inTemplate = false
        regexAllowed = true
        afterDot = false
        i += 2
      } else {
        i += 1
      }
      continue
    }
    if (isScriptSpace(text, i)) {
      i += 1
      continue
    }
    if (code === SLASH) {
      const next = text.charCodeAt(i + 1)
      if (next === SLASH) {
        found.push({ offset: i, kind: slashSlashKind(text, i) })
        i = lineEnd(text, i, end)
        continue
      }
      if (next === STAR) {
        found.push({ offset: i, kind: slashStarKind(text, i) })
        i = skipPast(text, '*/', i + 2, end)
        continue
      }
      if (regexAllowed && regexBudget > 0) {
        const close = regexEnd(text, i + 1, end)
        const following = text.charCodeAt(close + 1)
        if (close >= 0 && following !== SLASH && following !== STAR) {
          i = skipScriptWord(text, close + 1, end)
          regexAllowed = false
          afterDot = false
          continue
        }
        regexBudget -= (close >= 0 ? close : lineEnd(text, i + 1, end)) - i
      }
      regexAllowed = false
      afterDot = false
      i += 1
      continue
    }
    if (code === APOSTROPHE || code === QUOTE) {
      i = quotedEnd(text, i, end, code)
      regexAllowed = false
      afterDot = false
      continue
    }
    if (code === BACKTICK) {
      inTemplate = true
      i += 1
      continue
    }
    if (code === OPEN_BRACE) {
      depth += 1
      regexAllowed = true
      afterDot = false
      i += 1
      continue
    }
    if (code === CLOSE_BRACE) {
      if (depth === 0 && holes.length > 0) {
        depth = holes.pop()
        inTemplate = true
      } else {
        depth = Math.max(0, depth - 1)
        regexAllowed = false
        afterDot = false
      }
      i += 1
      continue
    }
    if (isDigit(code) || (code === DOT && isDigit(text.charCodeAt(i + 1)))) {
      i = skipNumber(text, i, end)
      regexAllowed = false
      afterDot = false
      continue
    }
    if (isScriptWord(text, i)) {
      const stop = skipScriptWord(text, i, end)
      regexAllowed = !afterDot && REGEX_KEYWORDS.has(text.slice(i, stop))
      afterDot = false
      i = stop
      continue
    }
    if ((code === PLUS || code === MINUS) && text.charCodeAt(i + 1) === code) {
      regexAllowed = false
      afterDot = false
      i += 2
      continue
    }
    afterDot = code === DOT
    regexAllowed = REGEX_PUNCTUATORS.has(text[i])
    i += 1
  }
}

function scanCss(text, start, end, found) {
  let i = start
  while (i < end) {
    const code = text.charCodeAt(i)
    if (code === SLASH && text.charCodeAt(i + 1) === STAR) {
      found.push({ offset: i, kind: BLOCK_COMMENT })
      i = skipPast(text, '*/', i + 2, end)
    } else if (code === QUOTE || code === APOSTROPHE) {
      i = quotedEnd(text, i, end, code)
    } else {
      i += 1
    }
  }
}

function vueBlockAt(text, index) {
  for (const block of VUE_BLOCKS) {
    block.open.lastIndex = index
    if (block.open.test(text)) {
      return block
    }
  }
  return null
}

function openTagEnd(text, from, end) {
  let quote = 0
  for (let j = from; j < end; j += 1) {
    const code = text.charCodeAt(j)
    if (quote !== 0) {
      if (code === quote) {
        quote = 0
      }
    } else if (code === QUOTE || code === APOSTROPHE) {
      quote = code
    } else if (code === GREATER) {
      return j + 1
    }
  }
  return -1
}

function scanVue(text, found) {
  const end = text.length
  let i = 0
  while (i < end) {
    const open = text.indexOf('<', i)
    if (open < 0) {
      return
    }
    if (text.startsWith('<!--', open)) {
      found.push({ offset: open, kind: MARKUP_COMMENT })
      i = skipPast(text, '-->', open + 4, end)
      continue
    }
    const block = vueBlockAt(text, open)
    if (block === null) {
      i = open + 1
      continue
    }
    const contentStart = openTagEnd(text, open + 1, end)
    if (contentStart < 0) {
      return
    }
    if (text.charCodeAt(contentStart - 2) === SLASH) {
      i = contentStart
      continue
    }
    block.close.lastIndex = contentStart
    const close = block.close.exec(text)
    const contentEnd = close === null ? end : close.index
    if (block.script) {
      scanScript(text, contentStart, contentEnd, found, false)
    } else {
      scanCss(text, contentStart, contentEnd, found)
    }
    if (close === null) {
      return
    }
    i = skipPast(text, '>', close.index, end)
  }
}

function scanJson(text, found) {
  const end = text.length
  let i = 0
  while (i < end) {
    const code = text.charCodeAt(i)
    const next = text.charCodeAt(i + 1)
    if (code === QUOTE) {
      i = quotedEnd(text, i, end, QUOTE)
    } else if (code === SLASH && next === SLASH) {
      found.push({ offset: i, kind: LINE_COMMENT })
      i = lineEnd(text, i, end)
    } else if (code === SLASH && next === STAR) {
      found.push({ offset: i, kind: BLOCK_COMMENT })
      i = skipPast(text, '*/', i + 2, end)
    } else {
      i += 1
    }
  }
}

function scanMarkup(text, found) {
  const end = text.length
  let i = 0
  while (i < end) {
    const open = text.indexOf('<', i)
    if (open < 0) {
      return
    }
    if (text.startsWith('<![CDATA[', open)) {
      i = skipPast(text, ']]>', open + 9, end)
    } else if (text.startsWith('<!--', open)) {
      found.push({ offset: open, kind: MARKUP_COMMENT })
      i = skipPast(text, '-->', open + 4, end)
    } else {
      i = open + 1
    }
  }
}

function scanMarkdown(text, found) {
  let open = text.indexOf('<!--')
  while (open >= 0) {
    found.push({ offset: open, kind: MARKUP_COMMENT })
    open = text.indexOf('<!--', open + 4)
  }
}

function nestedBlockEnd(text, from, end) {
  let depth = 1
  let j = from
  while (j < end) {
    const code = text.charCodeAt(j)
    const next = text.charCodeAt(j + 1)
    if (code === SLASH && next === STAR) {
      depth += 1
      j += 2
    } else if (code === STAR && next === SLASH) {
      depth -= 1
      j += 2
      if (depth === 0) {
        return j
      }
    } else {
      j += 1
    }
  }
  return end
}

function isEscapeString(text, index) {
  const prefix = text.charCodeAt(index - 1)
  if (prefix !== UPPER_E && prefix !== LOWER_E) {
    return false
  }
  return index < 2 || !isSqlWordCode(text.charCodeAt(index - 2))
}

function sqlStringEnd(text, index, end, backslashEscapes) {
  let j = index + 1
  while (j < end) {
    const code = text.charCodeAt(j)
    if (backslashEscapes && code === BACKSLASH) {
      j += 2
    } else if (code === APOSTROPHE && text.charCodeAt(j + 1) === APOSTROPHE) {
      j += 2
    } else if (code === APOSTROPHE) {
      return j + 1
    } else {
      j += 1
    }
  }
  return end
}

function scanSql(text, found) {
  const end = text.length
  let i = 0
  while (i < end) {
    const code = text.charCodeAt(i)
    const next = text.charCodeAt(i + 1)
    if (code === MINUS && next === MINUS) {
      found.push({ offset: i, kind: LINE_COMMENT })
      i = lineEnd(text, i, end)
    } else if (code === SLASH && next === STAR) {
      found.push({ offset: i, kind: BLOCK_COMMENT })
      i = nestedBlockEnd(text, i + 2, end)
    } else if (code === APOSTROPHE) {
      i = Math.min(sqlStringEnd(text, i, end, isEscapeString(text, i)), end)
    } else {
      i += 1
    }
  }
}

function opensQuote(text, index, lineStart) {
  return index === lineStart || !LETTER_OR_DIGIT.test(text[index - 1])
}

function scanHash(text, found) {
  eachLine(text, (start, stop) => {
    let quote = 0
    let j = start
    while (j < stop) {
      const code = text.charCodeAt(j)
      if (quote === APOSTROPHE) {
        if (code === APOSTROPHE && j + 1 < stop && text.charCodeAt(j + 1) === APOSTROPHE) {
          j += 2
          continue
        }
        if (code === APOSTROPHE) {
          quote = 0
        }
      } else if (quote === QUOTE) {
        if (code === BACKSLASH) {
          j += 2
          continue
        }
        if (code === QUOTE) {
          quote = 0
        }
      } else if (code === HASH) {
        const before = text.charCodeAt(j - 1)
        if (j === start || before === SPACE || before === TAB) {
          found.push({ offset: j, kind: LINE_COMMENT })
          return
        }
      } else if ((code === APOSTROPHE || code === QUOTE) && opensQuote(text, j, start)) {
        quote = code
      }
      j += 1
    }
  })
}

function scanHashLineStart(text, found) {
  eachLine(text, (start, stop) => {
    if (start < stop && text.charCodeAt(start) === HASH) {
      found.push({ offset: start, kind: LINE_COMMENT })
    }
  })
}

function scanHashAfterBlanks(text, found) {
  eachLine(text, (start, stop) => {
    let j = start
    while (j < stop && (text.charCodeAt(j) === SPACE || text.charCodeAt(j) === TAB)) {
      j += 1
    }
    if (j < stop && text.charCodeAt(j) === HASH) {
      found.push({ offset: j, kind: LINE_COMMENT })
    }
  })
}

function scanHashOrSemicolon(text, found) {
  eachLine(text, (start, stop) => {
    let j = start
    while (j < stop && (text.charCodeAt(j) === SPACE || text.charCodeAt(j) === TAB)) {
      j += 1
    }
    const code = text.charCodeAt(j)
    if (j < stop && (code === HASH || code === SEMICOLON)) {
      found.push({ offset: j, kind: LINE_COMMENT })
    }
  })
}

const SCANNERS = Object.freeze({
  csharp: scanCsharp,
  script: (text, found) => scanScript(text, 0, text.length, found, true),
  vue: scanVue,
  css: (text, found) => scanCss(text, 0, text.length, found),
  json: scanJson,
  markup: scanMarkup,
  markdown: scanMarkdown,
  sql: scanSql,
  hash: scanHash,
  hashLineStart: scanHashLineStart,
  hashAfterBlanks: scanHashAfterBlanks,
  hashOrSemicolon: scanHashOrSemicolon,
})

function lineStarts(text) {
  const starts = [0]
  for (let index = 0; index < text.length; index += 1) {
    const code = text.charCodeAt(index)
    if (code === CR && text.charCodeAt(index + 1) === LF) {
      index += 1
      starts.push(index + 1)
    } else if (isBreak(code)) {
      starts.push(index + 1)
    }
  }
  return starts
}

function locate(starts, offset) {
  let low = 0
  let high = starts.length - 1
  while (low < high) {
    const middle = (low + high + 1) >> 1
    if (starts[middle] <= offset) {
      low = middle
    } else {
      high = middle - 1
    }
  }
  return { line: low + 1, column: offset - starts[low] + 1 }
}

function findComments(name, data) {
  if (data.length === 0 || data.includes(0)) {
    return []
  }
  const family = classify(name)
  if (family === SKIP) {
    return []
  }
  if (family === UNCLASSIFIED) {
    return [{ line: 1, column: 1, kind: UNCLASSIFIED_FILE_TYPE }]
  }
  const decoded = data.toString('utf8')
  const text = decoded.charCodeAt(0) === BOM ? decoded.slice(1) : decoded
  const found = []
  SCANNERS[family](text, found)
  if (found.length === 0) {
    return []
  }
  found.sort((left, right) => left.offset - right.offset)
  const starts = lineStarts(text)
  return found.map(({ offset, kind }) => ({ ...locate(starts, offset), kind }))
}

function describe(error) {
  if (error instanceof Error && error.message) {
    return error.message
  }
  return String(error)
}

function firstLine(text) {
  const trimmed = text.trim()
  const eol = trimmed.indexOf('\n')
  return eol < 0 ? trimmed : trimmed.slice(0, eol)
}

function chomp(text) {
  if (text.endsWith('\r\n')) {
    return text.slice(0, -2)
  }
  return text.endsWith('\n') ? text.slice(0, -1) : text
}

function isMissing(error) {
  return error !== null && typeof error === 'object' && ['ENOENT', 'ENOTDIR'].includes(error.code)
}

function runGit(args, cwd, input) {
  const result = spawnSync('git', args, {
    cwd,
    input,
    env: { ...process.env, ...GIT_ENVIRONMENT },
    maxBuffer: GIT_MAX_BUFFER,
    windowsHide: true,
  })
  const command = `git ${args.join(' ')}`
  if (result.error) {
    throw new ScanError(`${command}: ${result.error.message}`)
  }
  if (result.signal) {
    throw new ScanError(`${command}: terminated by ${result.signal}`)
  }
  if (result.status !== 0) {
    const detail = firstLine(result.stderr.toString('utf8'))
    throw new ScanError(`${command}: ${detail === '' ? `exit ${result.status}` : detail}`)
  }
  return result.stdout
}

function repositoryTop() {
  const top = chomp(runGit(TOP_LEVEL_ARGUMENTS, process.cwd()).toString('utf8'))
  if (top === '') {
    throw new ScanError('the current folder is not inside a git work tree')
  }
  return top
}

function splitNul(buffer) {
  return buffer
    .toString('utf8')
    .split('\0')
    .filter((item) => item !== '')
}

function readWorktreeFile(file) {
  try {
    if (!fs.lstatSync(file).isFile()) {
      return null
    }
    return fs.readFileSync(file)
  } catch (error) {
    if (isMissing(error)) {
      return null
    }
    throw error
  }
}

function worktreeTargets() {
  const top = repositoryTop()
  const listed = [...new Set(splitNul(runGit(LIST_ARGUMENTS, top)))]
  return listed.map((rel) => ({
    display: rel,
    name: path.posix.basename(rel),
    load: () => readWorktreeFile(path.join(top, ...rel.split('/'))),
  }))
}

function parseRaw(buffer) {
  const tokens = buffer.toString('utf8').split('\0')
  const entries = []
  let index = 0
  while (index < tokens.length) {
    const header = tokens[index]
    if (header === '') {
      index += 1
      continue
    }
    const fields = header.startsWith(':') ? header.slice(1).split(' ') : []
    if (fields.length !== 5) {
      throw new ScanError(`unexpected git diff output ${JSON.stringify(header)}`)
    }
    const status = fields[4]
    const count = status.startsWith('R') || status.startsWith('C') ? 2 : 1
    const target = tokens[index + count]
    if (target === undefined || target === '') {
      throw new ScanError(`git diff output ends after ${JSON.stringify(header)}`)
    }
    entries.push({ mode: fields[1], oid: fields[3], path: target })
    index += count + 1
  }
  return entries
}

function readBlobs(top, oids) {
  const blobs = new Map()
  for (let first = 0; first < oids.length; first += BLOB_BATCH) {
    const batch = oids.slice(first, first + BLOB_BATCH)
    const out = runGit(BLOB_ARGUMENTS, top, `${batch.join('\n')}\n`)
    let offset = 0
    for (const oid of batch) {
      const eol = out.indexOf(LF, offset)
      if (eol < 0) {
        throw new ScanError(`git cat-file output ends before ${oid}`)
      }
      const fields = out.toString('latin1', offset, eol).split(' ')
      const size = Number(fields[2])
      const start = eol + 1
      if (fields.length !== 3 || fields[0] !== oid || fields[1] !== 'blob') {
        throw new ScanError(`staged object ${oid} is not a readable blob`)
      }
      if (!Number.isSafeInteger(size) || start + size >= out.length || out[start + size] !== LF) {
        throw new ScanError(`git cat-file output for ${oid} is truncated`)
      }
      blobs.set(oid, out.subarray(start, start + size))
      offset = start + size + 1
    }
  }
  return blobs
}

function realOrNull(target) {
  try {
    return fs.realpathSync.native(target)
  } catch {
    return null
  }
}

function insideRelative(root, target) {
  if (root === null || target === null) {
    return null
  }
  const relative = path.relative(root, target)
  if (relative === '') {
    return ''
  }
  if (relative === '..' || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) {
    return null
  }
  return relative.split(path.sep).join('/')
}

function covers(scope, file) {
  return scope === '' || file === scope || file.startsWith(`${scope}/`)
}

function exists(target) {
  try {
    fs.lstatSync(target)
    return true
  } catch {
    return false
  }
}

function stagedScopes(top, givenPaths, entries) {
  const realTop = realOrNull(top)
  return givenPaths.map((given) => {
    const absolute = path.resolve(given)
    const scope =
      insideRelative(top, absolute) ??
      insideRelative(realTop, absolute) ??
      insideRelative(realTop, realOrNull(absolute))
    if (scope === null) {
      throw new UsageError(`${given}: the path is outside the repository`)
    }
    if (!exists(absolute) && !entries.some((entry) => covers(scope, entry.path))) {
      throw new UsageError(`${given}: no such file`)
    }
    return scope
  })
}

function stagedTargets(givenPaths) {
  const top = repositoryTop()
  const staged = parseRaw(runGit(DIFF_ARGUMENTS, top))
  const scopes = givenPaths.length > 0 ? stagedScopes(top, givenPaths, staged) : null
  const entries = staged.filter(
    (entry) =>
      entry.mode !== GITLINK_MODE &&
      entry.mode !== SYMLINK_MODE &&
      (scopes === null || scopes.some((scope) => covers(scope, entry.path))) &&
      classify(path.posix.basename(entry.path)) !== SKIP,
  )
  const blobs = readBlobs(top, [...new Set(entries.map((entry) => entry.oid))])
  return entries.map((entry) => ({
    display: entry.path,
    name: path.posix.basename(entry.path),
    load: () => blobs.get(entry.oid),
  }))
}

function positionalTargets(givenPaths) {
  const unique = [...new Set(givenPaths)]
  for (const given of unique) {
    let stats = null
    try {
      stats = fs.statSync(given)
    } catch (error) {
      const code = error !== null && typeof error === 'object' ? error.code : undefined
      throw new UsageError(`${given}: no such file${code ? ` (${code})` : ''}`)
    }
    if (!stats.isFile()) {
      throw new UsageError(`${given}: not a regular file`)
    }
  }
  return unique.map((given) => ({
    display: given,
    name: path.basename(given),
    load: () => fs.readFileSync(given),
  }))
}

function parseArguments(args) {
  let staged = false
  let optionsEnded = false
  const paths = []
  for (const arg of args) {
    if (optionsEnded) {
      paths.push(arg)
    } else if (arg === END_OF_OPTIONS) {
      optionsEnded = true
    } else if (arg === STAGED_FLAG) {
      staged = true
    } else if (arg.startsWith('-')) {
      throw new UsageError(`unknown flag ${JSON.stringify(arg)}`)
    } else {
      paths.push(arg)
    }
  }
  return { staged, paths }
}

function collectTargets(options) {
  if (options.staged) {
    return stagedTargets(options.paths)
  }
  if (options.paths.length > 0) {
    return positionalTargets(options.paths)
  }
  return worktreeTargets()
}

function findingLines(targets) {
  const lines = []
  for (const target of targets) {
    if (classify(target.name) === SKIP) {
      continue
    }
    const data = target.load()
    if (data === null || data === undefined) {
      continue
    }
    const findings = findComments(target.name, data)
    if (findings.length > 0 && (target.display.includes('\n') || target.display.includes('\r'))) {
      throw new ScanError(
        `cannot report ${JSON.stringify(target.display)}: a line break in the path`,
      )
    }
    for (const finding of findings) {
      lines.push(`${target.display}:${finding.line}:${finding.column} ${finding.kind}`)
    }
  }
  return lines
}

function main(args) {
  try {
    const lines = findingLines(collectTargets(parseArguments(args)))
    if (lines.length === 0) {
      return EXIT_CLEAN
    }
    process.stdout.write(`${lines.join('\n')}\n`)
    return EXIT_FOUND
  } catch (error) {
    const usage = error instanceof UsageError ? `\n${USAGE}` : ''
    process.stderr.write(`${PROGRAM}: ${describe(error)}${usage}\n`)
    return EXIT_ERROR
  }
}

process.stdout.on('error', (error) => {
  if (error.code !== 'EPIPE') {
    process.stderr.write(`${PROGRAM}: ${describe(error)}\n`)
    process.exitCode = EXIT_ERROR
  }
})

process.exitCode = main(process.argv.slice(2))

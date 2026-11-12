import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {loadingQuotes} from '../../runtime/wwwroot/loading-quotes.js';

test('loading stream fills, holds its half step and retains the incoming row',()=>{
  const stream=loadingQuotes('one two three\nfour five six',12,2,2,false,()=>0);
  assert.equal(stream.advance(0),true);
  assert.equal(stream.halfStep,true);
  assert.equal(stream.lines.length,3);
  const incoming=stream.lines.at(-1);
  assert.equal(stream.advance(.03),false);
  assert.equal(stream.advance(.061),true);
  assert.equal(stream.halfStep,false);
  assert.equal(stream.lines.length,2);
  assert.equal(stream.lines.at(-1),incoming);
});
test('grandma mode omits marked quotes and strips markers otherwise',()=>{
  const safe=loadingQuotes('# comment\nmarked (\nordinary',100,100,2,true,()=>0);
  safe.advance(0);
  assert.ok(safe.lines.join(' ').includes('ordinary'));
  assert.ok(!safe.lines.join(' ').includes('marked'));
  const all=loadingQuotes('marked (',100,100,2,false,()=>0);
  all.advance(0);
  assert.ok(all.lines.join(' ').includes('marked'));
  assert.ok(!all.lines.join(' ').includes('('));
});

// At scale 16 the native frame cap is one token (0.75 words/second).
// Two-second steps let selection tests observe one token at a time.
const singleWordStream=(source,columns=100,random=()=>0,grandma=false)=>
  loadingQuotes(source,columns,100,16,grandma,random);
const words=stream=>stream.lines.join(' ').split(' ').filter(Boolean);
const tick=(stream,count)=>{
  for(let i=0;i<count;i++)stream.advance(i*2);
  return words(stream);
};

test('selection ranks the fitting word prefix, not total quote length',()=>{
  const stream=singleWordStream('a b c\nNinechars tail\nUnbreakablylong first',9);
  assert.deepEqual(tick(stream,2),['Ninechars','tail']);
  // Remaining capacity is four, including the separator after "tail".
  stream.advance(4);
  stream.advance(6);
  assert.deepEqual(stream.lines,['Ninechars','tail a b']);
});

test('a separator consumes the final cell when ranking the next quote',()=>{
  const stream=singleWordStream('abcde\nx\nyz',7);
  assert.deepEqual(tick(stream,2),['abcde','x']);
  assert.deepEqual(stream.lines,['abcde x']);
});

test('full and overfull rows rank the next quote against a new line',()=>{
  for(const first of ['abcdefghij','abcdefghijkl']) {
    const stream=singleWordStream(`start ${first}\nx\nzzzzz`,10);
    assert.deepEqual(tick(stream,3),['start',first,'zzzzz']);
    assert.deepEqual(stream.lines,['start',first,'zzzzz']);
  }
});

test('equal best fits use the independent random callback',()=>{
  assert.deepEqual(tick(singleWordStream('one\ntwo',3,()=>0),1),['one']);
  assert.deepEqual(tick(singleWordStream('one\ntwo',3,()=>.999),1),['two']);
});

test('history excludes quote values, releases only the oldest needed, and recovers small pools',()=>{
  const stream=singleWordStream('aa\naa\nbb',2);
  assert.deepEqual(tick(stream,6),['aa','bb','aa','bb','aa','bb']);
});

test('history retains twelve quotes and expires the oldest on the thirteenth',()=>{
  const pool=Array.from({length:13},(_,i)=>String(i).padStart(2,'0'));
  const stream=singleWordStream(pool.join('\n'),2);
  assert.deepEqual(tick(stream,14),[...pool,'00']);
});

test('source parsing matches StreamReader line endings, BOM, and comment rules',()=>{
  const stream=singleWordStream('\uFEFF# comment\r\n\t\r# hidden\n first\rsecond\r\nthird',6);
  assert.deepEqual(tick(stream,3),['second','first','third']);
  // A leading space makes this content rather than a comment in native LoadTips.
  assert.deepEqual(tick(singleWordStream(' #visible'),1),['#visible']);
});

test('marker matching is ordinal and only removes the exact trailing marker',()=>{
  for(const grandma of [false,true]) {
    const stream=singleWordStream('safe(\nkept ( \nfiltered (',100,()=>0,grandma);
    const output=tick(stream,8);
    assert.ok(output.includes('safe('));
    assert.ok(output.includes('(')); // The trailing space prevents marker matching.
    assert.equal(output.includes('filtered'),!grandma);
  }
});

test('empty, comments-only, fully filtered, and stripped-empty pools use native fallback',()=>{
  for(const source of ['', '# comment\r\n  ', 'hidden (', ' (']) {
    const stream=singleWordStream(source,100,()=>0,true);
    assert.deepEqual(stream.lines,[]);
    assert.deepEqual(tick(stream,2),['Loading.','Loading.']);
  }
  assert.deepEqual(tick(singleWordStream(' ('),2),['Loading.','Loading.']);
});

test('wrapping normalizes spaces, keeps long words intact and fits exact cell boundaries',()=>{
  const stream=singleWordStream('  aa   bb toolong c ',5);
  tick(stream,4);
  assert.deepEqual(stream.lines,['aa bb','toolong','c']);
});

test('first fill, fractional budgets and stalled frames obey the native frame cap',()=>{
  const stream=loadingQuotes('x',10000,100,2,false,()=>0);
  stream.advance(10);
  assert.equal(words(stream).length,48);
  assert.equal(stream.advance(10),false);
  assert.equal(stream.advance(10.01),false);
  assert.equal(stream.advance(10.03125),true);
  assert.equal(words(stream).length,49);
  stream.advance(1000);
  assert.equal(words(stream).length,97);
});

test('capacity scales with both UI dimensions and clamps scale below one half',()=>{
  for(const [scale,expected] of [[2,48],[1,192],[.5,768],[.1,768],[16,1]]) {
    const stream=loadingQuotes('x',10000,100,scale,false,()=>0);
    stream.advance(0);
    assert.equal(words(stream).length,expected,`scale ${scale}`);
    stream.advance(1000);
    assert.equal(words(stream).length,expected*2,`stalled frame at scale ${scale}`);
  }
});

test('backwards clock changes do not subtract the word budget',()=>{
  const stream=singleWordStream('x');
  stream.advance(10);
  assert.equal(stream.advance(9),false);
  assert.equal(stream.advance(10),false);
  assert.equal(stream.advance(11),true);
  assert.deepEqual(words(stream),['x','x']);
});

test('half-step completion paints alone and bounds catch-up before the next overflow',()=>{
  const stream=loadingQuotes('x',1,1,2,false,()=>0);
  stream.advance(0);
  assert.deepEqual(stream.lines,['x','x']);
  assert.equal(stream.halfStep,true);
  assert.equal(stream.advance(.059),false);
  assert.equal(stream.advance(.06),true);
  assert.equal(stream.scrolls,1);
  assert.equal(stream.halfStep,false);
  assert.deepEqual(stream.lines,['x']);
  stream.advance(1000);
  assert.equal(stream.halfStep,true);
  assert.deepEqual(stream.lines,['x','x']);
  stream.advance(2000);
  assert.equal(stream.scrolls,2);
  assert.deepEqual(stream.lines,['x']);
  stream.advance(2000);
  assert.equal(stream.halfStep,true);
  assert.deepEqual(stream.lines,['x','x']);
});

test('shipped quote streams stay within the authored mode-filtered word vocabulary',()=>{
  const source=readFileSync(new URL('../../../shared/loading-tips.txt',import.meta.url),'utf8');
  for(const grandma of [false,true]) {
    const allowed=new Set(source.split('\n').filter(line=>line.trim() && !line.startsWith('#'))
      .filter(line=>!grandma || !line.endsWith(' ('))
      .flatMap(line=>(line.endsWith(' (')?line.slice(0,-2):line).split(' ').filter(Boolean)));
    const stream=loadingQuotes(source,40,4,2,grandma,()=>0);
    for(let frame=0;frame<200;frame++) {
      stream.advance(frame*.125);
      assert.ok(stream.lines.length<=5);
      assert.ok(words(stream).every(word=>allowed.has(word)));
    }
    assert.ok(stream.scrolls>10);
  }
});

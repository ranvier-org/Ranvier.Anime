// The emitted JavaScript of the bindings and of the call sites in Fixtures.fs: object literals with only the given keys,
// and no currying or object-construction helpers.
import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";

const generated = new URL("./generated/", import.meta.url);
const bindings = new URL("src/Fable.Ranvier.Anime/", generated);
const helpers = /\b(curry\d*|uncurry\d*|partialApply|createObj)\b/g;
const read = url => readFileSync(url, "utf8");
const compact = text => text.replace(/\s+/g, "");
const fixtures = read(new URL("Fixtures.js", generated)).replace(/\r\n/g, "\n");
const surface = read(new URL("Surface.js", generated));
const body = name => compact(new RegExp(`export function ${name}\\([^)]*\\) \\{\\n([\\s\\S]*?)\\n\\}\\n`).exec(fixtures)[1]);

test("the erased binding files emit no module and the others are emitted", () => {
  assert.deepEqual(readdirSync(bindings).sort(), ["Css.js", "Params.js", "Reactive.js", "Testing.js"]);
});

test("no bindings module or fixture holds a currying or object-construction helper", () => {
  for (const file of readdirSync(bindings)) assert.deepEqual(read(new URL(file, bindings)).match(helpers), null, file);
  assert.deepEqual(fixtures.match(helpers), null, "Fixtures.js");
  assert.deepEqual(surface.match(helpers), null, "Surface.js");
});

test("parameters emit an object literal holding only the given arguments", () => {
  assert.equal(body("single"), "returnanimate(el,{x:1,});");
  assert.equal(body("sized"), 'returnanimate(el,{x:100,opacity:[0,1],duration:400,ease:"outQuart",});');
});

test("values, staggers and eases emit their anime forms", () => {
  assert.equal(body("mixed"), 'returnanimate(el,{y:"50%",rotate:{from:45},scale:0.9,delay:stagger(20,{start:100,}),});');
  assert.equal(body("spring"), "returnanimate(el,{x:1,ease:spring_1({stiffness:200,}),});");
});

test("callbacks emit as plain functions", () => {
  assert.equal(body("callbacks"), 'returnanimate(el,{x:1,modifier:(v)=>(v*2),onComplete:(_arg)=>{log("done");},});');
});

test("With assigns the property on the literal once", () => {
  assert.equal(body("custom"), 'returnanimate(el,Object.assign(({duration:300,}),{["--hue"]:120}));');
});

test("timeline members chain on the timeline with their positions", () => {
  assert.equal(body("timeline"), "return(createTimeline({autoplay:false,}).add(el,({x:1,}),('+='+50))).label(\"end\");");
});

test("namespace and engine members call through anime's exports", () => {
  const compacted = compact(surface);
  assert.ok(compacted.includes("returnwaapi_1.animate(el,parameters);"), "Waapi.animate");
  assert.ok(compacted.includes("engine_1.speed=2;engine_1.update();"), "Engine");
  assert.ok(compacted.includes('returnget$(el,"x",false);'), "Utils.getNumber");
});

test("numeric array parameters emit plain arrays", () => {
  assert.equal(body("bounds"), "return{container:[0,100,50,0],snap:[0,10],};");
  assert.equal(body("gridFrom"), "returnanimate(el,{delay:stagger(10,{from:[0.5,0.5],grid:[3,3],}),});");
  for (const [name, text] of [["Fixtures.js", fixtures], ["Surface.js", surface]]) assert.deepEqual(text.match(/\b(Int32|Float64)Array\b/g), null, name);
});

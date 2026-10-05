import assert from "node:assert/strict";
import { test } from "node:test";
import { readFile } from "node:fs/promises";
import { normalizeHtmlLf } from "./html-normalization.ts";
test("public HTML normalization removes CR/CRCR but leaves literal JavaScript escapes intact",()=>{assert.equal(normalizeHtmlLf('<head>\r\n</head>\r\r\n<script>"\\r"</script>\r'),'<head>\n</head>\n<script>"\\r"</script>\n');});
test("HTML source uses LF and Vite registers normalization after HTML transforms",async()=>{assert.equal((await readFile(new URL("../index.html",import.meta.url),"utf8")).includes("\r"),false);const config=await readFile(new URL("../vite.config.ts",import.meta.url),"utf8");assert.match(config,/order:\s*"post"\s*,\s*handler:\s*normalizeHtmlLf/);});

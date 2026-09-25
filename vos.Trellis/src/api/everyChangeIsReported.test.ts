import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';

/**
 * Every change a person asks Trellis for is reported to the broker, which tells the people the model
 * names. A write added without `apiClient.action` would be the one change nobody hears of, and nothing
 * else would fail to say so.
 *
 * A write is an `apiClient.post`, `put` or `del`. Each must sit inside an `apiClient.action(` begun on its
 * own line or one of the two above it, or be one of the reads below that travel as a POST.
 */
const SOURCE = resolve(__dirname, '..');
const WRITE = /apiClient\.(post|put|del)\s*</;

const READS_SENT_AS_POSTS: Record<string, string> = {
  "'/api/temporal/aggregate'": 'a reduction over Things by time bucket',
  "'/api/temporal/reduce'": "a reduction over one property's history",
  "'/api/ranges/validate'": 'checks criteria and changes nothing',
  'apiClient.post<unknown>(endpoint, body)': "a binding's read from a service; a widget's press is reported by postToEndpoint",
};

function sourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((name) => {
    const path = join(directory, name);
    if (statSync(path).isDirectory()) return sourceFiles(path);
    return /\.tsx?$/.test(name) && !/\.test\.tsx?$/.test(name) ? [path] : [];
  });
}

function unreportedWrites(): string[] {
  return sourceFiles(SOURCE).flatMap((file) => {
    const lines = readFileSync(file, 'utf8').split('\n');
    return lines.flatMap((line, index) => {
      if (!WRITE.test(line)) return [];
      if (lines.slice(Math.max(0, index - 2), index + 1).some((near) => near.includes('apiClient.action('))) return [];
      if (Object.keys(READS_SENT_AS_POSTS).some((read) => line.includes(read))) return [];
      return [`${relative(SOURCE, file)}:${index + 1}: ${line.trim()}`];
    });
  });
}

describe('every change a person asks for is reported (TC #7270)', () => {
  it('no write goes to the broker unreported', () => {
    expect(unreportedWrites()).toEqual([]);
  });

  it('the guard finds the writes it is about', () => {
    const writes = sourceFiles(SOURCE).filter((file) => WRITE.test(readFileSync(file, 'utf8')));
    expect(writes.length).toBeGreaterThan(0);
  });
});

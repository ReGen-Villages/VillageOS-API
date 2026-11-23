import { describe, expect, it } from 'vitest';
import { everyFigureReads, figureRefusals, figuresFrom, typedFigure } from './typedFigure';

describe('typedFigure', () => {
  it('reads an empty box as no figure given, not as a refusal', () => {
    expect(typedFigure('', true)).toEqual({ value: null });
    expect(typedFigure('   ', false)).toEqual({ value: null });
  });

  it('reads a figure as typed, around any spaces', () => {
    expect(typedFigure(' 320 ', true)).toEqual({ value: 320 });
    expect(typedFigure('2.4', false)).toEqual({ value: 2.4 });
    expect(typedFigure('0', true)).toEqual({ value: 0 });
  });

  it('reads a trailing decimal point as the figure before it', () => {
    expect(typedFigure('2.', false)).toEqual({ value: 2 });
  });

  it.each(['abc', '-', '1e400'])('refuses %s as not a number', (typed) => {
    expect(typedFigure(typed, false)).toEqual({ refusal: 'notANumber' });
  });

  it('refuses a negative figure, whole or not', () => {
    expect(typedFigure('-3', true)).toEqual({ refusal: 'negative' });
    expect(typedFigure('-2.5', false)).toEqual({ refusal: 'negative' });
  });

  it('refuses a decimal where a whole number is asked for, and only there', () => {
    expect(typedFigure('12.5', true)).toEqual({ refusal: 'notWhole' });
    expect(typedFigure('12.5', false)).toEqual({ value: 12.5 });
  });
});

describe('the figures of how many people the land is for', () => {
  it('names the reason for each figure that cannot be read', () => {
    expect(figureRefusals({ population: '12.5', householdSize: 'abc' }))
      .toEqual({ population: 'notWhole', householdSize: 'notANumber' });
    expect(figureRefusals({ population: '320', householdSize: '' })).toEqual({});
  });

  it('reads only while no figure is refused', () => {
    expect(everyFigureReads({ population: '320', householdSize: '2.4' })).toBe(true);
    expect(everyFigureReads({ population: '', householdSize: '' })).toBe(true);
    expect(everyFigureReads({ population: '320', householdSize: '-1' })).toBe(false);
  });

  it('posts the figures that were given and read, and leaves the rest out', () => {
    expect(figuresFrom({ population: '320', householdSize: '2.4' })).toEqual({ population: 320, householdSize: 2.4 });
    expect(figuresFrom({ population: '', householdSize: '2.4' })).toEqual({ householdSize: 2.4 });
    expect(figuresFrom({ population: '12.5', householdSize: 'abc' })).toEqual({});
  });
});

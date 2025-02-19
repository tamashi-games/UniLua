﻿namespace UniLua {
  public enum TK {
    // reserved words
    AND = 257,
    BREAK,
    DO,
    ELSE,
    ELSEIF,
    END,
    FALSE,
    FOR,
    FUNCTION,
    GOTO,
    IF,
    IN,
    LOCAL,
    NIL,
    NOT,
    OR,
    REPEAT,
    RETURN,
    THEN,
    TRUE,
    UNTIL,
    WHILE,

    // other terminal symbols
    CONCAT,
    DOTS,
    EQ,
    GE,
    LE,
    NE,
    DBCOLON,
    NUMBER,
    STRING,
    NAME,
    EOS,
  }
}

grammar DesignScript;

program:
    ( coreStmt | funcDef | imperFuncDef | emptyStmt )*
    ;

coreStmt:
    assignStmt
|   exprStmt
|   returnStmt ';'
|   imperBlock
|   imperBlockReturnStmt
|   imperBlockAssignStmt
    ;

funcDef:
    DEF typedIdent '(' funcDefArgList? ')' '{' coreStmt* '}'
    ;

imperFuncDef:
    imperAnnot DEF typedIdent '(' funcDefArgList? ')' '{' imperBlockBody '}'
    ;

emptyStmt : ';' ;

funcDefArgList:
    funcDefArg (',' funcDefArg)*
    ;

funcDefArg:
    typedIdent ('=' expr)?
    ;

expr:
    primary                                                     #primaryExpr
|   expr LBRACK expr RBRACK                                     #indexExpr
|   expr repGuideList                                           #repGuideExpr
|   expr atLevel repGuideList?                                  #atLevelExpr
|   qualifiedIdent '(' exprList? ')'                            #funcCallExpr
|   expr '.' Ident                                              #fieldExpr
|   ('!'|'+'|'-') expr                                          #unaryExpr
|   expr Op=('*'|'/'|'%') expr                                  #mulDivModExpr
|   expr Op=('+'|'-') expr                                      #addSubExpr
|   expr Op=('<=' | '>=' | '>' | '<') expr                      #compExpr
|   expr Op=('=='|'!=') expr                                    #eqExpr
|   expr '&&' expr                                              #andExpr
|   expr '||' expr                                              #orExpr
|   expr DOTDOT countstep='#'? expr DOTDOT count='#'? expr      #triRangeExpr
|   expr DOTDOT expr                                            #unitRangeExpr
|   expr '?' expr ':' expr                                      #inlineConditionExpr
    ;

primary:
    parExpr        #parenExpr
|   lit            #litExpr
|   Ident          #ident
    ;

parExpr:
    '(' expr ')'
    ;

lit:
     DoubleLit                            #doubleLit
|    IntLit                               #intLit
|    StringLit                            #stringLit
|    BoolLit                              #boolLit
|    NULL                                 #nullLit
|    LBRACK exprList? RBRACK              #listLit
|    '{' keyValues? '}'                   #dictLit
    ;

exprList:
    expr (',' expr)*
    ;

atLevel:
    Level
    ;

repGuideList:
    RepGuide+
    ;

exprStmt:
    expr ';'
    ;

returnStmt:
    RETURN ('='? exprList)?
    ;

imperAnnot:
    LBRACK 'Imperative' RBRACK
    ;

imperBlock:
    imperAnnot '{' imperBlockBody '}'
    ;

imperBlockReturnStmt:
    RETURN '='? imperBlock
    ;

imperBlockAssignStmt:
    typedIdentList '=' imperBlock
    ;

imperBlockBody:
    imperStmt*
    ;

imperStmt:
     coreStmt                                      #imperCoreStmt
|    IF '(' expr ')' imperStmt (ELSE imperStmt)?   #ifStmt
|    FOR '(' Ident IN expr ')' imperStmt           #forStmt
|    WHILE '(' expr ')' imperStmt                  #whileStmt
|    BREAK ';'                                     #breakStmt
|    CONTINUE ';'                                  #continueStmt
|    '{' imperBlockBody '}'                        #blockStmt
    ;

assignStmt:
    typedIdentList '=' expr ';'
    ;

typedIdent:
    Ident (':' typeNameWithRank)?
    ;

keyValue:
    StringLit ':' expr
    ;

keyValues:
    keyValue (',' keyValue)*
    ;

typedIdentList:
    typedIdent (',' typedIdent)*
    ;

identList:
    qualifiedIdent (',' qualifiedIdent)*
    ;

qualifiedIdent:
    Ident ('.' Ident)*
    ;

typeNameWithRank:
    typeName LBRACK RBRACK DOTDOT LBRACK RBRACK     #typeNameArbitraryRank
|   typeName LBRACK RBRACK DOTDOT                   #typeNameRankOneOrMore
|   typeName (LBRACK RBRACK)*                       #typeNameRank
    ;

typeName:
    qualifiedIdent
|   ERROR
|   BOOL
|   DOUBLE
|   INT_
|   STRING
|   VAR
    ;

// keywords must precede identifier tokens
// otherwise you'll get identifiers with keyword names :(
// The name "INT_" is used for the integer token so that it doesn't conflict with the "INT" type in ANTLR

VAR: 'var';
NEW: 'new';
ERROR: 'error';
BOOL: 'bool';
STRING: 'string';
DOUBLE: 'double';
INT_: 'int';
DEF : 'def';
IN : 'in';
IF : 'if';
ELSE : 'else';
NULL : 'null';
RETURN : 'return';
FOR: 'for';
WHILE: 'while';
BREAK: 'break';
CONTINUE: 'continue';

BoolLit:
    'true'
|   'false'
    ;

DoubleLit:
    INT '.' [0-9]+ EXP? | INT EXP
    ;

IntLit:
    INT
    ;
    
Level:
    ('@' | '@@') 'L' INT
    ;

RepGuide:
    '<' INT 'L'? '>'
    ;

fragment INT:
    '0' | [1-9] [0-9]*
    ;

fragment EXP:
    [Ee] [+\-]? INT
    ;

StringLit
    :   '"' StringChars? '"'
    ;

fragment StringChars:
    StringChar+
    ;

fragment StringChar:
    ~["\\]
|   EscapeSeq
    ;

fragment EscapeSeq:
    '\\' [abtnfrv"'\\]
|   UnicodeEscape
|   HexByteEscape
    ;

fragment HexByteEscape
    :   '\\' 'x' HexDigit HexDigit
    ;

fragment UnicodeEscape
    :   '\\' 'u' HexDigit HexDigit HexDigit HexDigit
    ;

fragment HexDigit
    :   [0-9a-fA-F]
    ;

Ident:
    IdentStartChar IdentChar*
    ;

fragment IdentChar
   : IdentStartChar
   | '0'..'9'
   | '\u00B7'
   | '\u0300'..'\u036F'
   | '\u203F'..'\u2040'
   ;

fragment IdentStartChar
   : 'A'..'Z' | 'a'..'z'
   | '$'
   | '_'
   | '\u00C0'..'\u00D6'
   | '\u00D8'..'\u00F6'
   | '\u00F8'..'\u02FF'
   | '\u0370'..'\u037D'
   | '\u037F'..'\u1FFF'
   | '\u200C'..'\u200D'
   | '\u2070'..'\u218F'
   | '\u2C00'..'\u2FEF'
   | '\u3001'..'\uD7FF'
   | '\uF900'..'\uFDCF'
   | '\uFDF0'..'\uFFFD'
   ;

WS
    : [ \t\n\r] + -> skip
    ;

COMMENT
    : '/*' .*? '*/' -> skip
    ;

LINE_COMMENT
    : '//' ~[\r\n]* -> skip
    ;

LBRACK: '[';
RBRACK: ']';
DOTDOT: '..';
ADDADD    : '++';
SUBSUB    : '--';
ADD    : '+';
SUB    : '-';
MUL    : '*';
DIV    : '/';
MOD    : '%';
NOT    : '!';
AND    : '&&';
OR    : '||';
GT    : '>';
GE    : '>=';
LT    : '<';
LE    : '<=';
NE    : '!=';
EQ    : '==';
## 1.1.0

- Made the stack overflow prevention less expensive (((probably)))

- The methodology for preventing dynamic skin distortion in the CSS has been completely overhauled
  - Applying the base skin to prevent conflicts is now done synchronously, preventing any race conditions that could have possibly existed in the past
<br></br>
    - The base skin is only applied first if a potential conflict is actually detected, rather than all the time
<br></br>
  - Fixed old (RuneFox implementation) dynamic skins distorting when switching to one from a new (KingEnderBrine implementation) dynamic skin

## 1.0.10

- Changed a method name <3

## 1.0.9

- Nullcheck everything!!!

## 1.0.8

- Fixed dynamic skins distorting in CSS when they are selected consecutively

## 1.0.7

- README changes

## 1.0.6

- Fixed a typo
  - Whoops! Silly me! Whooops! Uh oh!

## 1.0.5

- Yet further fixes for Acrid skins

## 1.0.4

- Further fixes for Acrid skins

## 1.0.3

- Added a potential fix for some Acrid skins

## 1.0.2

- Added a potential fix for some Engineer skins

## 1.0.1

- Minor icon adjustment

## 1.0.0

- Initial release

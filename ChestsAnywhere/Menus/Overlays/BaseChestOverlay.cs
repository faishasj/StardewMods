using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pathoschild.Stardew.Automate.Framework;
using Pathoschild.Stardew.ChestsAnywhere.Framework;
using Pathoschild.Stardew.ChestsAnywhere.Menus.Components;
using Pathoschild.Stardew.ChestsAnywhere.Menus.Search;
using Pathoschild.Stardew.Common;
using Pathoschild.Stardew.Common.UI;
using Pathoschild.Stardew.Common.Utilities;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;

namespace Pathoschild.Stardew.ChestsAnywhere.Menus.Overlays;

/// <summary>The base overlay for a menu which lets the player navigate and edit containers.</summary>
internal abstract class BaseChestOverlay : BaseOverlay, IStorageOverlay
{
    /*********
    ** Fields
    *********/
    /****
    ** Constants
    ****/
    private const int EditButtonId = 1_200_000;
    private const int ChestDropdownId = EditButtonId + 10;
    private const int CategoryDropdownId = EditButtonId + 11;
    private const int EditNameFieldId = EditButtonId + 1;
    private const int EditCategoryFieldId = EditButtonId + 2;
    private const int EditOrderFieldId = EditButtonId + 3;
    private const int EditHideChestFieldId = EditButtonId + 4;
    private const int EditAutomateStoreId = EditButtonId + 5;
    private const int EditAutomateTakeId = EditButtonId + 6;
    private const int EditSaveButtonId = EditButtonId + 7;
    private const int EditResetButtonId = EditButtonId + 8;
    private const int EditExitButtonId = EditButtonId + 9;

    /****
    ** Data
    ****/
    /// <summary>The available chests.</summary>
    private readonly ManagedChest[] Chests;

    /// <summary>The selected chest.</summary>
    private readonly ManagedChest Chest;

    /// <summary>Whether to show Automate options if applicable for this chest type.</summary>
    private readonly bool ShowAutomateOptions;

    /// <summary>The number of draw cycles since the menu was initialized.</summary>
    private int DrawCount;

    /// <summary>Get whether the menu and its components have been initialized.</summary>
    protected bool IsInitialized => this.DrawCount > 1;

    /// <summary>The unique chest categories.</summary>
    private readonly string[] Categories;

    /// <summary>The name of the selected category.</summary>
    private string SelectedCategory => this.Chest.DisplayCategory;

    /// <summary>The mod configuration.</summary>
    protected ModConfig Config { get; }

    /// <summary>The configured key bindings.</summary>
    private readonly ModConfigKeys Keys;

    /// <summary>The keybind for escaping the active element or menu.</summary>
    private readonly KeybindList EscapeKeybind = KeybindList.Parse($"{SButton.Escape}, {SButton.ControllerB}");

    /// <summary>The controller inputs which should be handled by the active menu's snappy-menu navigation.</summary>
    private readonly HashSet<SButton> ControllerNavigationButtons =
    [
        SButton.DPadUp,
        SButton.DPadDown,
        SButton.DPadLeft,
        SButton.DPadRight,
        SButton.LeftThumbstickUp,
        SButton.LeftThumbstickDown,
        SButton.LeftThumbstickLeft,
        SButton.LeftThumbstickRight,
        SButton.ControllerA
    ];

    /****
    ** Menu management
    ****/
    /// <summary>The underlying chest menu.</summary>
    private readonly IClickableMenu Menu;

    /// <summary>Whether the chest menu is ready to close.</summary>
    protected bool CanCloseChest => this.Menu.readyToClose();

    /****
    ** Access UI
    ****/
    /// <summary>The font with which to render text.</summary>
    private readonly SpriteFont Font = Game1.smallFont;

    /// <summary>The chest dropdown.</summary>
    protected Dropdown<ManagedChest>? ChestDropdown;

    /// <summary>The category dropdown.</summary>
    protected Dropdown<string>? CategoryDropdown;

    /// <summary>The edit button.</summary>
    protected ClickableTextureComponent EditButton;

    /// <summary>The original upward neighbor IDs for menu components linked to the overlay's top navigation row.</summary>
    private readonly Dictionary<ClickableComponent, int> OriginalUpNeighborIds = [];

    /// <summary>The original downward neighbor IDs for menu components linked to the overlay's top navigation row.</summary>
    private readonly Dictionary<ClickableComponent, int> OriginalDownNeighborIds = [];

    /// <summary>Whether the controller component graph needs to be rebuilt after drawing.</summary>
    private bool ControllerFlowNeedsRefresh = true;

    /// <summary>The component to focus after rebuilding the controller component graph.</summary>
    private ClickableComponent? PendingControllerFocus;

    /// <summary>The components currently injected into the underlying menu's controller graph.</summary>
    private readonly List<ClickableComponent> ControllerComponents = [];

    /****
    ** Edit UI
    ****/
    /// <summary>The editable chest name.</summary>
    private ValidatedTextBox EditNameField;

    /// <summary>The field order.</summary>
    private ValidatedTextBox EditOrderField;

    /// <summary>The editable category name.</summary>
    private ValidatedTextBox EditCategoryField;

    /// <summary>A checkbox which indicates whether to hide the chest.</summary>
    private Checkbox EditHideChestField;

    /// <summary>A dropdown which configures how Automate stores items in this chest, if applicable.</summary>
    private SimpleDropdown<AutomateContainerPreference>? EditAutomateStore;

    /// <summary>A dropdown which configures how Automate takes items from this chest, if applicable.</summary>
    private SimpleDropdown<AutomateContainerPreference>? EditAutomateTake;

    /// <summary>The clickable area which saves the edit form.</summary>
    private ClickableComponent EditSaveButtonArea;

    /// <summary>The clickable area which clears data for the edit form.</summary>
    private ClickableComponent EditResetButtonArea;

    /// <summary>The top-right button which closes the edit form.</summary>
    private ClickableTextureComponent EditExitButton;

    /// <summary>The controller-selectable area for the chest name field.</summary>
    private ClickableComponent EditNameFieldArea;

    /// <summary>The controller-selectable area for the category field.</summary>
    private ClickableComponent EditCategoryFieldArea;

    /// <summary>The controller-selectable area for the order field.</summary>
    private ClickableComponent EditOrderFieldArea;

    /// <summary>The controller-selectable area for the hide-chest checkbox.</summary>
    private ClickableComponent EditHideChestFieldArea;

    /// <summary>The controller-selectable area for the Automate storage dropdown.</summary>
    private ClickableComponent? EditAutomateStoreArea;

    /// <summary>The controller-selectable area for the Automate take dropdown.</summary>
    private ClickableComponent? EditAutomateTakeArea;

    /// <summary>The textboxes managed by Chests Anywhere.</summary>
    private IEnumerable<ValidatedTextBox> ManagedTextboxes => [this.EditNameField, this.EditOrderField, this.EditCategoryField];

    /// <summary>The keybind which pastes text into a selected textbox.</summary>
    /// <remarks>Derived from <see cref="KeyboardDispatcher.Poll"/>.</remarks>
    private readonly KeybindList PasteKey = new KeybindList(Constants.TargetPlatform == GamePlatform.Mac
        ? [new Keybind(SButton.LeftWindows, SButton.V), new Keybind(SButton.RightWindows, SButton.V)]
        : [new Keybind(SButton.LeftControl, SButton.V), new Keybind(SButton.RightControl, SButton.V)]
    );

    /// <summary>The modifier key that must be held to activate the <see cref="PasteKey"/>.</summary>
    private readonly HashSet<SButton> PasteModifierKeys = Constants.TargetPlatform == GamePlatform.Mac
        ? [SButton.LeftWindows, SButton.RightWindows]
        : [SButton.LeftControl, SButton.RightControl];


    /*********
    ** Accessors
    *********/
    /// <inheritdoc />
    public Element ActiveElement
    {
        get;
        protected set
        {
            Element previous = field;
            field = value;
            this.OnActiveElementChanged(previous, value);
        }
    }

    /// <inheritdoc />
    public event Action<ManagedChest>? OnChestSelected;

    /// <inheritdoc />
    public event Action<ManagedChest>? OnAutomateOptionsChanged;


    /*********
    ** Public methods
    *********/
    /// <summary>Open the search menu.</summary>
    private void ShowChestSearch()
    {
        Game1.activeClickableMenu = new ChestSearchMenu(this.Chest, this.Chests, this.Keys);
    }

    /// <summary>Sort the player's inventory.</summary>
    public void SortInventory()
    {
        ItemGrabMenu.organizeItemsInList(Game1.player.Items);
        Game1.playSound("Ship");
    }

    /// <summary>Switch to the specified chest.</summary>
    /// <param name="chest">The chest to select.</param>
    public void SelectChest(ManagedChest chest)
    {
        this.OnChestSelected?.Invoke(chest);
    }

    /// <inheritdoc cref="IStorageOverlay.Dispose" />
    public override void Dispose()
    {
        this.RemoveControllerComponents();
        if (Game1.activeClickableMenu == this.Menu)
            this.Menu.populateClickableComponentList();

        this.OnAutomateOptionsChanged = null;
        this.OnChestSelected = null;
        base.Dispose();
    }


    /*********
    ** Protected methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="menu">The underlying chest menu.</param>
    /// <param name="chest">The selected chest.</param>
    /// <param name="chests">The available chests.</param>
    /// <param name="config">The mod configuration.</param>
    /// <param name="keys">The configured key bindings.</param>
    /// <param name="events">The SMAPI events available for mods.</param>
    /// <param name="input">An API for checking and changing input state.</param>
    /// <param name="reflection">Simplifies access to private code.</param>
    /// <param name="showAutomateOptions">Whether to show Automate options if applicable for this chest type.</param>
    /// <param name="keepAlive">Indicates whether to keep the overlay active. If <c>null</c>, the overlay is kept until explicitly disposed.</param>
    protected BaseChestOverlay(IClickableMenu menu, ManagedChest chest, ManagedChest[] chests, ModConfig config, ModConfigKeys keys, IModEvents events, IInputHelper input, IReflectionHelper reflection, bool showAutomateOptions, Func<bool> keepAlive)
        : base(events, input, reflection, keepAlive, assumeUiMode: true)
    {
        // data
        this.ShowAutomateOptions = showAutomateOptions;

        // menu
        this.Menu = menu;

        // chests & config
        this.Chest = chest;
        this.Chests = chests;
        this.Categories = chests.Select(p => p.DisplayCategory).Distinct().OrderBy(p => p, HumanSortComparer.DefaultIgnoreCase).ToArray();
        this.Config = config;
        this.Keys = keys;

        this.ReinitializeBaseComponents();
    }

    /// <inheritdoc />
    protected override void DrawUi(SpriteBatch batch)
    {
        if (this.DrawCount == 0)
            this.ReinitializeComponents();

        this.DrawCount++;
        Rectangle bounds = new Rectangle(this.Menu.xPositionOnScreen, this.Menu.yPositionOnScreen, this.Menu.width, this.Menu.height);

        // access mode
        if (!this.ActiveElement.HasFlag(Element.EditForm))
        {
            float navOpacity = this.CanCloseChest ? 1f : 0.5f;

            // dropdowns
            this.ChestDropdown?.Draw(batch, navOpacity);
            this.CategoryDropdown?.Draw(batch, navOpacity);

            // edit button
            this.EditButton.draw(batch, Color.White * navOpacity, 1f);
        }

        // edit mode
        else
        {
            // get translations
            string locationLabel = I18n.Label_Location() + ":";
            string nameLabel = I18n.Label_Name() + ":";
            string categoryLabel = I18n.Label_Category() + ":";
            string orderLabel = I18n.Label_Order() + ":";

            // get initial measurements
            SpriteFont font = Game1.smallFont;
            const int gutter = 10;
            int padding = Game1.pixelZoom * 10;
            float topOffset = padding;
            int maxLabelWidth = (int)new[] { locationLabel, nameLabel, categoryLabel, orderLabel }.Select(p => font.MeasureString(p).X).Max();

            // background
            batch.DrawMenuBackground(new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height));

            // Location name
            {
                string locationName = this.Chest.Location.Name;
                if (this.Chest.Tile != Vector2.Zero)
                    locationName += " (" + I18n.Label_Location_Tile(x: this.Chest.Tile.X, y: this.Chest.Tile.Y) + ")";

                Vector2 labelSize = batch.DrawTextBlock(font, locationLabel, new Vector2(bounds.X + padding + (int)(maxLabelWidth - font.MeasureString(locationLabel).X), bounds.Y + topOffset), bounds.Width);
                batch.DrawTextBlock(font, locationName, new Vector2(bounds.X + padding + maxLabelWidth + gutter, bounds.Y + topOffset), bounds.Width);
                topOffset += labelSize.Y;
            }

            // editable text fields
            var fields = new[] { Tuple.Create(nameLabel, this.EditNameField), Tuple.Create(categoryLabel, this.EditCategoryField), Tuple.Create(orderLabel, this.EditOrderField) };
            foreach (var field in fields)
            {
                // get data
                string label = field.Item1;
                ValidatedTextBox textbox = field.Item2;

                // draw label
                Vector2 labelSize = batch.DrawTextBlock(font, label, new Vector2(bounds.X + padding + (int)(maxLabelWidth - font.MeasureString(label).X), bounds.Y + topOffset + 7), bounds.Width);
                textbox.X = bounds.X + padding + maxLabelWidth + gutter;
                textbox.Y = bounds.Y + (int)topOffset;
                textbox.Draw(batch);

                // update offset
                topOffset += Math.Max(labelSize.Y + 7, textbox.Height);
            }

            // checkboxes
            topOffset += this.DrawAndPositionCheckbox(batch, font, this.EditHideChestField, bounds.X + padding, bounds.Y + (int)topOffset, this.EditHideChestField.Value ? I18n.Label_HideChestHidden() : I18n.Label_HideChest()).Y;

            // buttons
            void DrawButtons(int yOffset)
            {
                this.DrawButton(batch, Game1.smallFont, this.EditSaveButtonArea, bounds.X + padding, bounds.Y + (int)topOffset + yOffset, I18n.Button_Save(), Color.DarkGreen, out Rectangle saveButtonBounds);
                this.DrawButton(batch, Game1.smallFont, this.EditResetButtonArea, bounds.X + padding + saveButtonBounds.Width + 2, bounds.Y + (int)topOffset + yOffset, I18n.Button_Reset(), Color.DarkRed, out _);
            }

            // Automate options
            // note: must be drawn in reverse order (including the buttons), so dropdowns overlay the elements below them
            if (this.EditAutomateStore != null || this.EditAutomateTake != null)
            {
                int storageDropdownHeight = this.EditAutomateStore?.Bounds.Height ?? 0;
                int fetchDropdownHeight = this.EditAutomateTake?.Bounds.Height ?? 0;

                // label
                topOffset += padding;
                topOffset += batch.DrawTextBlock(Game1.smallFont, I18n.Label_AutomateOptions(), new Vector2(bounds.X + padding, bounds.Y + topOffset), wrapWidth: bounds.Width - bounds.X - padding, bold: true).Y;

                // buttons
                DrawButtons(yOffset: storageDropdownHeight + fetchDropdownHeight + padding);

                // dropdowns
                this.EditAutomateTake?.Draw(batch, bounds.X + padding, bounds.Y + (int)topOffset + storageDropdownHeight);
                this.EditAutomateStore?.Draw(batch, bounds.X + padding, bounds.Y + (int)topOffset);
            }
            else
                DrawButtons(yOffset: 0);


            // exit button
            this.EditExitButton.draw(batch);
        }

        this.UpdateControllerFlow();

        // cursor
        this.DrawCursor();
    }

    /****
    ** Event handlers
    ****/
    /// <inheritdoc />
    protected override void ReceiveGameWindowResized()
    {
        this.ReinitializeComponents();
    }

    /// <inheritdoc cref="IInputEvents.ButtonsChanged" />
    protected override void ReceiveButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (!this.IsInitialized)
            return;

        if (e.Pressed.Contains(SButton.ControllerA) && this.TryActivateSnappedControllerComponent())
        {
            this.InputHelper.Suppress(SButton.ControllerA);
            return;
        }

        // check for textbox focus
        bool anyTextboxSelected = Game1.game1.HasKeyboardFocus() && Game1.game1.instanceKeyboardDispatcher?.Subscriber != null;

        // handle keys
        bool canNavigate = this.CanCloseChest && !anyTextboxSelected;
        ModConfigKeys keys = this.Keys;
        switch (this.ActiveElement)
        {
            case Element.Menu:
                if (keys.Toggle.JustPressed() || this.EscapeKeybind.JustPressed())
                {
                    this.InputHelper.SuppressActiveKeybinds(keys.Toggle);
                    this.InputHelper.SuppressActiveKeybinds(this.EscapeKeybind);

                    if (canNavigate)
                        this.Exit();
                }
                else if (keys.OpenSearchMenu.JustPressed() && canNavigate)
                    this.ShowChestSearch();
                else if (keys.PrevChest.JustPressed() && canNavigate)
                    this.SelectPreviousChest();
                else if (keys.NextChest.JustPressed() && canNavigate)
                    this.SelectNextChest();
                else if (keys.PrevCategory.JustPressed() && canNavigate)
                    this.SelectPreviousCategory();
                else if (keys.NextCategory.JustPressed() && canNavigate)
                    this.SelectNextCategory();
                else if (keys.EditChest.JustPressed() && canNavigate)
                    this.OpenEdit();
                else if (keys.SortItems.JustPressed())
                    this.SortInventory();
                else
                    return; // don't suppress if the key wasn't handled

                this.SuppressAll(e.Pressed);
                break;

            case Element.ChestList:
            case Element.CategoryList:
            case Element.EditForm:
                if (this.EscapeKeybind.JustPressed())
                    this.ActiveElement = Element.Menu;

                if (this.ActiveElement == Element.EditForm && this.TryNavigateExpandedEditDropdown(e.Pressed))
                {
                    this.SuppressAll(e.Pressed);
                    break;
                }
                if (this.ActiveElement == Element.EditForm && e.Pressed.Any(this.IsControllerDirection))
                    this.DeselectManagedTextboxes();

                // always suppress in this context, since we'll handle text input ourselves
                if (e.Pressed.Any())
                {
                    // since we suppress all input, we need to handle CTRL + V ourselves
                    KeyboardDispatcher dispatcher = Game1.keyboardDispatcher;
                    if (dispatcher?.Subscriber != null && this.PasteKey.IsDown())
                    {
                        string? text = null;
                        DesktopClipboard.GetText(ref text);
                        if (text != null && !dispatcher.ShouldSuppress())
                            dispatcher.Subscriber.RecieveTextInput(Regex.Replace(text, @"[\r\n]", ""));
                    }

                    IEnumerable<SButton> buttonsToSuppress = e.Pressed.Except(this.PasteModifierKeys);
                    if (this.ActiveElement is Element.ChestList or Element.CategoryList or Element.EditForm)
                        buttonsToSuppress = buttonsToSuppress.Except(this.ControllerNavigationButtons);

                    this.SuppressAll(buttonsToSuppress);
                }

                break;
        }
    }

    /// <summary>Suppress all pressed buttons.</summary>
    /// <param name="buttons">The buttons to suppress.</param>
    protected void SuppressAll(IEnumerable<SButton> buttons)
    {
        foreach (SButton button in buttons)
            this.InputHelper.Suppress(button);
    }

    /// <inheritdoc />
    protected override bool ReceiveScrollWheelAction(int amount)
    {
        if (!this.IsInitialized)
            return false;

        switch (this.ActiveElement)
        {
            case Element.Menu:
                bool scrollNext = amount < 0;

                // scroll dropdowns
                if (this.Keys.HoldToMouseWheelScrollCategories.IsDown())
                {
                    if (scrollNext)
                        this.SelectNextCategory();
                    else
                        this.SelectPreviousCategory();
                    return true;
                }
                if (this.Keys.HoldToMouseWheelScrollChests.IsDown())
                {
                    if (scrollNext)
                        this.SelectNextChest();
                    else
                        this.SelectPreviousChest();
                }
                return false;

            case Element.ChestList:
                this.ChestDropdown?.ReceiveScrollWheelAction(amount);
                return true;

            case Element.CategoryList:
                this.CategoryDropdown?.ReceiveScrollWheelAction(amount);
                return true;

            case Element.EditForm:
                return true; // suppress input

            default:
                return false;
        }
    }

    /// <inheritdoc />
    protected override bool ReceiveLeftClick(int x, int y)
    {
        if (!this.IsInitialized)
            return false;

        switch (this.ActiveElement)
        {
            // edit form
            case Element.EditForm:
                // textboxes
                if (this.TryClickTextbox(x, y))
                {
                    // handled internally
                }

                // hide chest checkbox
                else if (this.EditHideChestField.GetBounds().Contains(x, y))
                    this.EditHideChestField.Toggle();

                // Automate options
                else if (this.EditAutomateStore?.TryClick(x, y) is true || this.EditAutomateTake?.TryClick(x, y) is true)
                {
                    // handled internally
                }

                // save button
                else if (this.EditSaveButtonArea.containsPoint(x, y))
                {
                    this.SaveEdit();
                    this.ActiveElement = Element.Menu;
                }

                // reset button
                else if (this.EditResetButtonArea.containsPoint(x, y))
                    this.ResetEdit();

                // exit button
                else if (this.EditExitButton.containsPoint(x, y))
                    this.ActiveElement = Element.Menu;

                return true; // handle all clicks while open

            // chest list
            case Element.ChestList:
                {
                    // select chest
                    if (this.ChestDropdown!.TryClick(x, y, out bool itemClicked, out bool dropdownToggled))
                    {
                        if (itemClicked)
                        {
                            this.SelectChest(this.ChestDropdown.Selected);
                            this.ReinitializeComponents();
                        }

                        if (dropdownToggled)
                            this.ActiveElement = this.ChestDropdown.IsExpanded ? Element.ChestList : Element.Menu;
                    }

                    // close dropdown
                    else
                        this.ActiveElement = Element.Menu;
                }
                return true; // handle all clicks while open

            // category list
            case Element.CategoryList:
                {
                    // select category
                    if (this.CategoryDropdown!.TryClick(x, y, out bool itemClicked, out bool dropdownToggled))
                    {
                        if (itemClicked)
                        {
                            string category = this.CategoryDropdown.Selected;
                            if (category != this.SelectedCategory)
                            {
                                this.SelectChest(this.Chests.First(chest => chest.DisplayCategory == category));
                                this.ReinitializeComponents();
                            }
                        }

                        if (dropdownToggled)
                            this.ActiveElement = this.CategoryDropdown.IsExpanded ? Element.CategoryList : Element.Menu;
                    }

                    // close dropdown
                    else
                        this.ActiveElement = Element.Menu;
                }
                return true; // handle all clicks while open

            // buttons & dropdown
            default:
                bool canNavigate = this.CanCloseChest;
                if (this.EditButton.containsPoint(x, y) && canNavigate)
                    this.OpenEdit();
                else if (this.ChestDropdown?.TryClick(x, y) is true && canNavigate)
                {
                    this.ChestDropdown.IsExpanded = true;
                    this.ActiveElement = Element.ChestList;
                }
                else if (this.CategoryDropdown?.TryClick(x, y) is true && canNavigate)
                {
                    this.CategoryDropdown.IsExpanded = true;
                    this.ActiveElement = Element.CategoryList;
                }
                else
                    return false;
                return true;
        }
    }

    /// <inheritdoc />
    protected override bool ReceiveCursorHover(int x, int y)
    {
        if (!this.IsInitialized)
            return false;

        switch (this.ActiveElement)
        {
            case Element.Menu:
                this.EditButton.tryHover(x, y);
                return false;

            case Element.EditForm:
                this.EditExitButton.tryHover(x, y);
                this.EditAutomateStore?.TryHover(x, y);
                this.EditAutomateTake?.TryHover(x, y);
                return true;

            case Element.ChestList:
            case Element.CategoryList:
                return true; // suppress menu hover

            default:
                return false;
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Initialize the base edit-chest overlay for rendering.</summary>
    [MemberNotNull(
        nameof(BaseChestOverlay.EditButton),
        nameof(BaseChestOverlay.EditNameField),
        nameof(BaseChestOverlay.EditCategoryField),
        nameof(BaseChestOverlay.EditOrderField),
        nameof(BaseChestOverlay.EditHideChestField),
        nameof(BaseChestOverlay.EditSaveButtonArea),
        nameof(BaseChestOverlay.EditResetButtonArea),
        nameof(BaseChestOverlay.EditExitButton),
        nameof(BaseChestOverlay.EditNameFieldArea),
        nameof(BaseChestOverlay.EditCategoryFieldArea),
        nameof(BaseChestOverlay.EditOrderFieldArea),
        nameof(BaseChestOverlay.EditHideChestFieldArea)
    )]
    private void ReinitializeBaseComponents()
    {
        bool editButtonWasFocused = this.ControllerComponents.Contains(this.Menu.currentlySnappedComponent);
        this.RemoveControllerComponents();

        int topOffset = this.GetTopOffset(this.Menu);

        Rectangle bounds = new Rectangle(this.Menu.xPositionOnScreen, this.Menu.yPositionOnScreen, this.Menu.width, this.Menu.height);
        int maxDropdownLabelWidth = (bounds.Width / 2) - 50;

        // category dropdown
        this.CategoryDropdown = null;
        if (this.Categories.Length > 1)
        {
            this.CategoryDropdown = new Dropdown<string>(bounds.Right - Game1.tileSize, bounds.Y, this.Font, this.SelectedCategory, this.Categories, category => category, maxDropdownLabelWidth);
            this.CategoryDropdown.myID = CategoryDropdownId;
            this.CategoryDropdown.ScreenReaderText = I18n.Label_Category();

            if (Constants.TargetPlatform != GamePlatform.Android)
                this.CategoryDropdown.bounds.Y = bounds.Y - this.CategoryDropdown.bounds.Height + topOffset;
            this.CategoryDropdown.bounds.X -= this.CategoryDropdown.bounds.Width; // right-align
            this.CategoryDropdown.ReinitializeComponents();
        }

        // chest dropdown
        this.ChestDropdown = null;
        {
            ManagedChest[] chests = this.Chests.Where(chest => chest.DisplayCategory == this.SelectedCategory).ToArray();
            if (chests.Length > 0)
            {
                ManagedChest? selected = ChestFactory.GetBestMatch(chests, this.Chest);
                this.ChestDropdown = new Dropdown<ManagedChest>(bounds.X, bounds.Y, this.Font, selected, chests, chest => chest.DisplayName, maxDropdownLabelWidth);
                this.ChestDropdown.myID = ChestDropdownId;
                this.ChestDropdown.ScreenReaderText = I18n.Label_Name();

                if (Constants.TargetPlatform != GamePlatform.Android)
                {
                    this.ChestDropdown.bounds.Y = bounds.Y - this.ChestDropdown.bounds.Height + topOffset;
                    this.ChestDropdown.ReinitializeComponents();
                }
            }
        }

        // edit chest button overlay (based on chest dropdown position)
        {
            Rectangle sprite = CommonSprites.Icons.SpeechBubble;
            float zoom = Game1.pixelZoom / 2f;

            Point iconPos = this.ChestDropdown != null
                ? new Point(this.ChestDropdown.bounds.X + this.ChestDropdown.bounds.Width, this.ChestDropdown.bounds.Y)
                : new Point(0, bounds.Y + topOffset);

            Rectangle buttonBounds = new Rectangle(iconPos.X, iconPos.Y, (int)(sprite.Width * zoom), (int)(sprite.Height * zoom));
            this.EditButton = new ClickableTextureComponent("edit-chest", buttonBounds, null, I18n.Button_EditChest(), CommonSprites.Icons.Sheet, sprite, zoom)
            {
                myID = EditButtonId
            };
        }

        // edit form
        int longTextWidth = (int)Game1.smallFont.MeasureString("A sufficiently, reasonably long string").X;
        this.EditNameField = new ValidatedTextBox(Game1.smallFont, Color.Black, ch => ch != '|') { Width = longTextWidth };
        this.EditCategoryField = new ValidatedTextBox(Game1.smallFont, Color.Black, ch => ch != '|') { Width = longTextWidth };
        this.EditOrderField = new ValidatedTextBox(Game1.smallFont, Color.Black, char.IsDigit) { Width = (int)Game1.smallFont.MeasureString("9999999").X };
        this.EditHideChestField = new Checkbox();

        if (this.ShowAutomateOptions && this.Chest.CanConfigureAutomateStore)
        {
            this.EditAutomateStore = new SimpleDropdown<AutomateContainerPreference>(
                this.Reflection,
                options: [
                    new KeyValuePair<AutomateContainerPreference, string>(AutomateContainerPreference.Allow, I18n.Label_AutomateStore()),
                    new KeyValuePair<AutomateContainerPreference, string>(AutomateContainerPreference.Prefer, I18n.Label_AutomateStoreFirst()),
                    new KeyValuePair<AutomateContainerPreference, string>(AutomateContainerPreference.Disable, I18n.Label_AutomateStoreDisabled())
                ]
            );
        }
        else
            this.EditAutomateStore = null;

        if (this.ShowAutomateOptions && this.Chest.CanConfigureAutomateTake)
        {
            this.EditAutomateTake = new SimpleDropdown<AutomateContainerPreference>(
                this.Reflection,
                options: [
                    new KeyValuePair<AutomateContainerPreference, string>(AutomateContainerPreference.Allow, I18n.Label_AutomateTake()),
                    new KeyValuePair<AutomateContainerPreference, string>(AutomateContainerPreference.Prefer, I18n.Label_AutomateTakeFirst()),
                    new KeyValuePair<AutomateContainerPreference, string>(AutomateContainerPreference.Disable, I18n.Label_AutomateTakeDisabled())
                ]
            );
        }
        else
            this.EditAutomateTake = null;

        this.FillForm();

        this.EditNameFieldArea = this.CreateControllerComponent(EditNameFieldId, "edit-name", I18n.Label_Name());
        this.EditCategoryFieldArea = this.CreateControllerComponent(EditCategoryFieldId, "edit-category", I18n.Label_Category());
        this.EditOrderFieldArea = this.CreateControllerComponent(EditOrderFieldId, "edit-order", I18n.Label_Order());
        this.EditHideChestFieldArea = this.CreateControllerComponent(EditHideChestFieldId, "edit-hide", I18n.Label_HideChest());
        this.EditAutomateStoreArea = this.EditAutomateStore != null
            ? this.CreateControllerComponent(EditAutomateStoreId, "edit-automate-store", I18n.Label_AutomateStore())
            : null;
        this.EditAutomateTakeArea = this.EditAutomateTake != null
            ? this.CreateControllerComponent(EditAutomateTakeId, "edit-automate-take", I18n.Label_AutomateTake())
            : null;
        this.EditSaveButtonArea = this.CreateControllerComponent(EditSaveButtonId, "save-chest", I18n.Button_Save());
        this.EditResetButtonArea = this.CreateControllerComponent(EditResetButtonId, "reset-chest", I18n.Button_Reset());
        this.EditExitButton = new ClickableTextureComponent(new Rectangle(bounds.Right - 9 * Game1.pixelZoom, bounds.Y - Game1.pixelZoom * 2, CommonSprites.Icons.ExitButton.Width * Game1.pixelZoom, CommonSprites.Icons.ExitButton.Height * Game1.pixelZoom), CommonSprites.Icons.Sheet, CommonSprites.Icons.ExitButton, Game1.pixelZoom)
        {
            myID = EditExitButtonId
        };

        this.ControllerFlowNeedsRefresh = true;
        this.PendingControllerFocus = this.ActiveElement == Element.EditForm
            ? this.EditNameFieldArea
            : editButtonWasFocused ? this.EditButton : null;
    }

    /// <summary>Initialize the edit-chest overlay for rendering.</summary>
    protected virtual void ReinitializeComponents()
    {
        this.ReinitializeBaseComponents();
    }

    /// <summary>Get the Y pixel offset to apply relative to <see cref="IClickableMenu.yPositionOnScreen"/> when drawing the top UI elements.</summary>
    /// <param name="menu">The menu for which the UI is being overlaid.</param>
    protected virtual int GetTopOffset(IClickableMenu menu)
    {
        return 0;
    }

    /// <summary>Set the form values to match the underlying chest.</summary>
    private void FillForm()
    {
        this.EditNameField.Text = this.Chest.DisplayName;
        this.EditCategoryField.Text = this.Chest.DisplayCategory;
        this.EditOrderField.Text = this.Chest.Order?.ToString() ?? string.Empty;
        this.EditHideChestField.Value = this.Chest.IsIgnored;
        this.EditAutomateStore?.TrySelect(this.Chest.AutomateStoreItems);
        this.EditAutomateTake?.TrySelect(this.Chest.AutomateTakeItems);
    }

    /// <summary>Reset the edit form to the default values.</summary>
    private void ResetEdit()
    {
        this.Chest.Reset();
        this.FillForm();
    }

    /// <summary>Save the form input.</summary>
    private void SaveEdit()
    {
        // get order value
        int? order = null;
        {
            if (int.TryParse(this.EditOrderField.Text, out int parsed))
                order = parsed;
        }

        // update chest
        AutomateContainerPreference automateStore = this.EditAutomateStore?.SelectedKey ?? this.Chest.AutomateStoreItems;
        AutomateContainerPreference automateTake = this.EditAutomateTake?.SelectedKey ?? this.Chest.AutomateTakeItems;
        bool automateChanged = automateStore != this.Chest.AutomateStoreItems || automateTake != this.Chest.AutomateTakeItems;
        this.Chest.Update(
            name: this.EditNameField.Text,
            category: this.EditCategoryField.Text,
            order: order,
            ignored: this.EditHideChestField.Value,
            automateStoreItems: automateStore,
            automateTakeItems: automateTake
        );
        this.OnChestSelected?.Invoke(this.Chest);
        if (automateChanged)
            this.OnAutomateOptionsChanged?.Invoke(this.Chest);
    }

    /// <summary>Handle the active element changing.</summary>
    /// <param name="previous">The previous value.</param>
    /// <param name="value">The new value.</param>
    private void OnActiveElementChanged(Element previous, Element value)
    {
        // disable readonly mode
        this.SetItemsClickable(value == Element.Menu);

        // close open dropdowns
        if (value != Element.CategoryList && value != Element.ChestList)
        {
            if (this.ChestDropdown != null)
                this.ChestDropdown.IsExpanded = false;
            if (this.CategoryDropdown != null)
                this.CategoryDropdown.IsExpanded = false;
        }

        // deselect textboxes
        if (value != Element.EditForm)
            this.DeselectManagedTextboxes();

        this.ControllerFlowNeedsRefresh = true;
        this.PendingControllerFocus = value switch
        {
            Element.EditForm => this.EditNameFieldArea,
            Element.ChestList => this.ChestDropdown,
            Element.CategoryList => this.CategoryDropdown,
            Element.Menu when previous == Element.ChestList => this.ChestDropdown,
            Element.Menu when previous == Element.CategoryList => this.CategoryDropdown,
            _ => this.EditButton
        };
    }

    /// <summary>Exit the chest menu.</summary>
    protected void Exit()
    {
        this.Dispose();
        this.Menu.exitThisMenu();
    }

    /// <summary>Update textboxes on the edit form for a click, if applicable.</summary>
    /// <param name="x">The X-position of the cursor.</param>
    /// <param name="y">The Y-position of the cursor.</param>
    /// <returns>Returns whether a textbox was clicked.</returns>
    private bool TryClickTextbox(int x, int y)
    {
        if (this.ActiveElement != Element.EditForm)
            return false;

        // select textbox
        foreach (ValidatedTextBox textbox in this.ManagedTextboxes)
        {
            if (!textbox.GetBounds().Contains(x, y))
                continue;

            if (!textbox.Selected)
            {
                this.DeselectManagedTextboxes();
                textbox.Select();
            }

            return true;
        }

        // else deselect any current textbox
        this.DeselectManagedTextboxes();
        return false;
    }

    /// <summary>Get the index of a chest in the selected category.</summary>
    /// <param name="chest">The chest to find.</param>
    /// <param name="chests">The chests to search.</param>
    private int GetChestIndex(ManagedChest chest, ManagedChest[] chests)
    {
        ManagedChest? match = ChestFactory.GetBestMatch(chests, chest);
        return match != null
            ? Array.IndexOf(chests, match)
            : -1;
    }

    /// <summary>Switch to the previous chest in the list.</summary>
    private void SelectPreviousChest()
    {
        ManagedChest[] chests = this.GetChestsFromCategory(this.SelectedCategory);
        int curIndex = this.GetChestIndex(this.Chest, chests);
        this.SelectChest(chests[curIndex != 0 ? curIndex - 1 : chests.Length - 1]);
    }

    /// <summary>Switch to the next chest in the list.</summary>
    private void SelectNextChest()
    {
        ManagedChest[] chests = this.GetChestsFromCategory(this.SelectedCategory);
        int curIndex = this.GetChestIndex(this.Chest, chests);
        this.SelectChest(chests[(curIndex + 1) % chests.Length]);
    }

    /// <summary>Switch to the previous category.</summary>
    private void SelectPreviousCategory()
    {
        int curIndex = Array.IndexOf(this.Categories, this.SelectedCategory);
        string category = this.Categories[curIndex != 0 ? curIndex - 1 : this.Categories.Length - 1];
        this.SelectChest(this.Chests.First(chest => chest.DisplayCategory == category));
    }

    /// <summary>Switch to the next category.</summary>
    private void SelectNextCategory()
    {
        int curIndex = Array.IndexOf(this.Categories, this.SelectedCategory);
        string category = this.Categories[(curIndex + 1) % this.Categories.Length];
        this.SelectChest(this.Chests.First(chest => chest.DisplayCategory == category));
    }

    /// <summary>Reset and display the edit screen.</summary>
    private void OpenEdit()
    {
        this.EditNameField.Text = this.Chest.DisplayName;
        this.EditCategoryField.Text = this.Chest.DisplayCategory;
        this.EditOrderField.Text = this.Chest.Order?.ToString() ?? string.Empty;
        this.EditHideChestField.Value = this.Chest.IsIgnored;
        this.EditAutomateStore?.TrySelect(this.Chest.AutomateStoreItems);
        this.EditAutomateTake?.TrySelect(this.Chest.AutomateTakeItems);

        this.ActiveElement = Element.EditForm;
    }

    /// <summary>Get the chests in a given category.</summary>
    /// <param name="category">The chest category.</param>
    private ManagedChest[] GetChestsFromCategory(string category)
    {
        return this.Chests.Where(chest => chest.DisplayCategory == category).ToArray();
    }

    /// <summary>Set whether the chest or inventory items should be clickable.</summary>
    /// <param name="clickable">Whether items should be clickable.</param>
    protected abstract void SetItemsClickable(bool clickable);

    /// <summary>Deselect textboxes managed by Chests Anywhere.</summary>
    private void DeselectManagedTextboxes()
    {
        foreach (ValidatedTextBox textbox in this.ManagedTextboxes)
            textbox.Selected = false;
    }

    /// <summary>Create a component used for controller navigation.</summary>
    private ClickableComponent CreateControllerComponent(int id, string name, string screenReaderText)
    {
        return new ClickableComponent(Rectangle.Empty, name)
        {
            myID = id,
            ScreenReaderText = screenReaderText
        };
    }

    /// <summary>Update the underlying menu's controller graph for the current overlay state.</summary>
    private void UpdateControllerFlow()
    {
        if (!Game1.options.SnappyMenus || !Game1.options.gamepadControls)
            return;

        this.UpdateEditFormControllerBounds();

        bool expectedComponentPresent = this.ActiveElement == Element.EditForm
            ? this.Menu.allClickableComponents?.Contains(this.EditNameFieldArea) is true
            : this.Menu.allClickableComponents?.Contains(this.EditButton) is true;
        if (!this.ControllerFlowNeedsRefresh && expectedComponentPresent)
            return;

        this.RestoreMenuControllerFlow();
        this.ControllerComponents.Clear();

        if (this.ActiveElement == Element.EditForm)
            this.InstallEditFormControllerFlow();
        else
            this.InstallMenuControllerFlow();

        if (this.PendingControllerFocus != null)
        {
            this.Menu.currentlySnappedComponent = this.PendingControllerFocus;
            this.Menu.snapCursorToCurrentSnappedComponent();
        }

        this.PendingControllerFocus = null;
        this.ControllerFlowNeedsRefresh = false;
    }

    /// <summary>Add the chest, edit, and category controls to the underlying menu's controller graph.</summary>
    private void InstallMenuControllerFlow()
    {
        this.Menu.populateClickableComponentList();
        this.Menu.allClickableComponents ??= [];

        List<ClickableComponent> topRow = [];
        if (this.ChestDropdown != null)
            topRow.Add(this.ChestDropdown);
        topRow.Add(this.EditButton);
        if (this.CategoryDropdown != null)
            topRow.Add(this.CategoryDropdown);
        topRow.Sort((left, right) => left.bounds.Center.X.CompareTo(right.bounds.Center.X));

        List<ClickableComponent> inventoryTopRow = this.GetInventoryTopRow();
        List<ClickableComponent> colorPickerBottomRow = this.GetColorPickerBottomRow();
        for (int i = 0; i < topRow.Count; i++)
        {
            ClickableComponent component = topRow[i];
            component.leftNeighborID = i > 0 ? topRow[i - 1].myID : -1;
            component.rightNeighborID = i + 1 < topRow.Count ? topRow[i + 1].myID : -1;
            component.upNeighborID = this.FindClosestByX(component, colorPickerBottomRow)?.myID ?? -1;

            bool isExpandedDropdown = component is Dropdown<ManagedChest> { IsExpanded: true }
                || component is Dropdown<string> { IsExpanded: true };
            if (!isExpandedDropdown)
                component.downNeighborID = this.FindClosestByX(component, inventoryTopRow)?.myID ?? -1;
        }

        foreach (ClickableComponent inventoryComponent in inventoryTopRow)
        {
            this.OriginalUpNeighborIds[inventoryComponent] = inventoryComponent.upNeighborID;
            inventoryComponent.upNeighborID = this.FindClosestByX(inventoryComponent, topRow)?.myID ?? inventoryComponent.upNeighborID;
        }

        foreach (ClickableComponent colorComponent in colorPickerBottomRow)
        {
            this.OriginalDownNeighborIds[colorComponent] = colorComponent.downNeighborID;
            colorComponent.downNeighborID = this.FindClosestByX(colorComponent, topRow)?.myID ?? colorComponent.downNeighborID;
        }

        this.ControllerComponents.AddRange(topRow);
        this.Menu.allClickableComponents.AddRange(topRow);

        Dropdown<ManagedChest>? expandedChestDropdown = this.ChestDropdown is { IsExpanded: true }
            ? this.ChestDropdown
            : null;
        Dropdown<string>? expandedCategoryDropdown = this.CategoryDropdown is { IsExpanded: true }
            ? this.CategoryDropdown
            : null;
        IEnumerable<ClickableComponent>? dropdownChildren = expandedChestDropdown?.GetChildComponents()
            ?? expandedCategoryDropdown?.GetChildComponents();
        ClickableComponent? expandedDropdown = expandedChestDropdown ?? (ClickableComponent?)expandedCategoryDropdown;
        if (dropdownChildren != null && expandedDropdown != null)
        {
            List<ClickableComponent> children = dropdownChildren.ToList();
            ClickableComponent? topChild = children.FirstOrDefault(child => child.myID == expandedDropdown.downNeighborID);
            if (topChild != null)
                topChild.upNeighborID = expandedDropdown.myID;

            this.ControllerComponents.AddRange(children);
            this.Menu.allClickableComponents.AddRange(children);
        }
    }

    /// <summary>Get the top row of the chest inventory for linking to the overlay controls.</summary>
    private List<ClickableComponent> GetInventoryTopRow()
    {
        IEnumerable<ClickableComponent> candidates = this.Menu is ItemGrabMenu itemGrabMenu
            ? itemGrabMenu.ItemsToGrabMenu.inventory
            : this.Menu.allClickableComponents.Where(component =>
            {
                Point center = component.bounds.Center;
                return component.myID >= 0
                    && center.X >= this.Menu.xPositionOnScreen
                    && center.X <= this.Menu.xPositionOnScreen + this.Menu.width
                    && center.Y > this.EditButton.bounds.Bottom;
            });

        int? topY = candidates.Select(component => (int?)component.bounds.Y).Min();
        return topY.HasValue
            ? candidates.Where(component => component.bounds.Y == topY.Value).OrderBy(component => component.bounds.X).ToList()
            : [];
    }

    /// <summary>Get the bottom row of the chest color picker, if shown.</summary>
    private List<ClickableComponent> GetColorPickerBottomRow()
    {
        if (this.Menu is not ItemGrabMenu { chestColorPicker.visible: true } itemGrabMenu)
            return [];

        IEnumerable<ClickableComponent> candidates = itemGrabMenu.discreteColorPickerCC
            .Where(component => component.visible && component.myID >= 0);
        int? bottomY = candidates.Select(component => (int?)component.bounds.Y).Max();
        return bottomY.HasValue
            ? candidates.Where(component => component.bounds.Y == bottomY.Value).OrderBy(component => component.bounds.X).ToList()
            : [];
    }

    /// <summary>Find the component whose horizontal center is nearest to the source component.</summary>
    private ClickableComponent? FindClosestByX(ClickableComponent source, IEnumerable<ClickableComponent> candidates)
    {
        return candidates.MinBy(component => Math.Abs(component.bounds.Center.X - source.bounds.Center.X));
    }

    /// <summary>Replace the underlying menu's controller graph with the edit form controls.</summary>
    private void InstallEditFormControllerFlow()
    {
        List<ClickableComponent> verticalComponents =
        [
            this.EditNameFieldArea,
            this.EditCategoryFieldArea,
            this.EditOrderFieldArea,
            this.EditHideChestFieldArea
        ];
        if (this.EditAutomateStoreArea != null)
            verticalComponents.Add(this.EditAutomateStoreArea);
        if (this.EditAutomateTakeArea != null)
            verticalComponents.Add(this.EditAutomateTakeArea);

        for (int i = 0; i < verticalComponents.Count; i++)
        {
            ClickableComponent component = verticalComponents[i];
            component.upNeighborID = i > 0 ? verticalComponents[i - 1].myID : EditExitButtonId;
            component.downNeighborID = i + 1 < verticalComponents.Count ? verticalComponents[i + 1].myID : EditSaveButtonId;
            component.leftNeighborID = -1;
            component.rightNeighborID = -1;
        }

        int lastFieldId = verticalComponents[^1].myID;
        this.EditSaveButtonArea.upNeighborID = lastFieldId;
        this.EditSaveButtonArea.downNeighborID = EditExitButtonId;
        this.EditSaveButtonArea.leftNeighborID = -1;
        this.EditSaveButtonArea.rightNeighborID = EditResetButtonId;

        this.EditResetButtonArea.upNeighborID = lastFieldId;
        this.EditResetButtonArea.downNeighborID = EditExitButtonId;
        this.EditResetButtonArea.leftNeighborID = EditSaveButtonId;
        this.EditResetButtonArea.rightNeighborID = -1;

        this.EditExitButton.upNeighborID = EditResetButtonId;
        this.EditExitButton.downNeighborID = EditNameFieldId;
        this.EditExitButton.leftNeighborID = EditResetButtonId;
        this.EditExitButton.rightNeighborID = -1;

        this.ControllerComponents.AddRange(verticalComponents);
        this.ControllerComponents.Add(this.EditSaveButtonArea);
        this.ControllerComponents.Add(this.EditResetButtonArea);
        this.ControllerComponents.Add(this.EditExitButton);
        this.Menu.allClickableComponents = [.. this.ControllerComponents];
    }

    /// <summary>Update controller component bounds to match the rendered edit controls.</summary>
    private void UpdateEditFormControllerBounds()
    {
        this.EditNameFieldArea.bounds = this.EditNameField.GetBounds();
        this.EditCategoryFieldArea.bounds = this.EditCategoryField.GetBounds();
        this.EditOrderFieldArea.bounds = this.EditOrderField.GetBounds();
        this.EditHideChestFieldArea.bounds = this.EditHideChestField.GetBounds();

        if (this.EditAutomateStoreArea != null && this.EditAutomateStore != null)
            this.EditAutomateStoreArea.bounds = this.EditAutomateStore.Bounds;
        if (this.EditAutomateTakeArea != null && this.EditAutomateTake != null)
            this.EditAutomateTakeArea.bounds = this.EditAutomateTake.Bounds;
    }

    /// <summary>Remove controller components injected into the underlying menu.</summary>
    private void RemoveControllerComponents()
    {
        this.RestoreMenuControllerFlow();

        if (this.Menu.allClickableComponents != null)
        {
            foreach (ClickableComponent component in this.ControllerComponents)
                this.Menu.allClickableComponents.Remove(component);
        }

        this.ControllerComponents.Clear();
    }

    /// <summary>Restore menu components changed to link to the overlay controls.</summary>
    private void RestoreMenuControllerFlow()
    {
        foreach ((ClickableComponent component, int upNeighborId) in this.OriginalUpNeighborIds)
            component.upNeighborID = upNeighborId;
        foreach ((ClickableComponent component, int downNeighborId) in this.OriginalDownNeighborIds)
            component.downNeighborID = downNeighborId;

        this.OriginalUpNeighborIds.Clear();
        this.OriginalDownNeighborIds.Clear();
    }

    /// <summary>Activate the overlay component currently selected by controller navigation.</summary>
    private bool TryActivateSnappedControllerComponent()
    {
        ClickableComponent? component = this.Menu.currentlySnappedComponent;
        if (component == null || !this.ControllerComponents.Contains(component))
            return false;

        Point center = component.bounds.Center;
        return this.ReceiveLeftClick(center.X, center.Y);
    }

    /// <summary>Get whether a button is directional controller input.</summary>
    private bool IsControllerDirection(SButton button)
    {
        return button is
            SButton.DPadUp
            or SButton.DPadDown
            or SButton.DPadLeft
            or SButton.DPadRight
            or SButton.LeftThumbstickUp
            or SButton.LeftThumbstickDown
            or SButton.LeftThumbstickLeft
            or SButton.LeftThumbstickRight;
    }

    /// <summary>Handle directional controller input for an expanded edit-form dropdown.</summary>
    private bool TryNavigateExpandedEditDropdown(IEnumerable<SButton> pressedButtons)
    {
        int offset = 0;
        foreach (SButton button in pressedButtons)
        {
            if (button is SButton.DPadUp or SButton.DPadLeft or SButton.LeftThumbstickUp or SButton.LeftThumbstickLeft)
                offset = -1;
            else if (button is SButton.DPadDown or SButton.DPadRight or SButton.LeftThumbstickDown or SButton.LeftThumbstickRight)
                offset = 1;
        }

        return offset != 0
            && (this.EditAutomateStore?.TryMoveSelection(offset) is true || this.EditAutomateTake?.TryMoveSelection(offset) is true);
    }

    /// <summary>Draw a checkbox to the screen, including any position updates needed.</summary>
    /// <param name="batch">The sprite batch being drawn.</param>
    /// <param name="font">The font to use for the checkbox label.</param>
    /// <param name="checkbox">The checkbox to draw.</param>
    /// <param name="x">The top-left X position to start drawing from.</param>
    /// <param name="y">The top-left Y position to start drawing from.</param>
    /// <param name="label">The translation for the checkbox label.</param>
    /// <param name="defaultValue">The default value.</param>
    private Vector2 DrawAndPositionCheckbox(SpriteBatch batch, SpriteFont font, Checkbox checkbox, int x, int y, string label, bool defaultValue = false)
    {
        checkbox.X = x;
        checkbox.Y = y;
        checkbox.Width = 24;
        checkbox.Draw(batch);
        Vector2 labelSize = batch.DrawTextBlock(font, label, new Vector2(x + 7 + checkbox.Width, y), this.Menu.width, checkbox.Value != defaultValue ? Color.Red : Color.Black);

        return new Vector2(checkbox.Width + 7 + checkbox.Width + labelSize.X, Math.Max(checkbox.Width, labelSize.Y));
    }

    /// <summary>Draw a checkbox to the screen, including any position updates needed.</summary>
    /// <param name="batch">The sprite batch being drawn.</param>
    /// <param name="font">The font to use for the checkbox label.</param>
    /// <param name="clickArea">The clickable area to draw.</param>
    /// <param name="x">The top-left X position to start drawing from.</param>
    /// <param name="y">The top-left Y position to start drawing from.</param>
    /// <param name="label">The translation for the checkbox label.</param>
    /// <param name="color">The text color to draw.</param>
    /// <param name="bounds">The button's outer bounds.</param>
    private void DrawButton(SpriteBatch batch, SpriteFont font, ClickableComponent clickArea, int x, int y, string label, in Color color, out Rectangle bounds)
    {
        // get text
        Vector2 labelSize = font.MeasureString(label);

        // draw button
        CommonHelper.DrawButton(batch, new Vector2(x, y), labelSize, out Vector2 contentPos, out bounds);
        Utility.drawBoldText(batch, label, font, contentPos, color);

        // align clickable area
        clickArea.bounds = bounds;

        // return size
        //return new Vector2(bounds.Width, bounds.Height);
    }
}

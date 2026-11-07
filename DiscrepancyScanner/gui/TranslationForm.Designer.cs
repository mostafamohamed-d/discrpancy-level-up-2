
namespace DiscrepancyScanner
{
    partial class TranslationForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.Ztext = new System.Windows.Forms.TextBox();
            this.Ytext = new System.Windows.Forms.TextBox();
            this.Xtext = new System.Windows.Forms.TextBox();
            this.PickPt = new System.Windows.Forms.Button();
            this.PickLabelX = new System.Windows.Forms.Label();
            this.PickLabelZ = new System.Windows.Forms.Label();
            this.PickLabelY = new System.Windows.Forms.Label();
            this.AngleGroupBox = new System.Windows.Forms.GroupBox();
            this.angleText = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.Okbut = new System.Windows.Forms.Button();
            this.mirrorGroupBox = new System.Windows.Forms.GroupBox();
            this.mirroredCheckY = new System.Windows.Forms.CheckBox();
            this.mirroredCheckX = new System.Windows.Forms.CheckBox();
            this.CancelBut = new System.Windows.Forms.Button();
            this.groupBox1.SuspendLayout();
            this.AngleGroupBox.SuspendLayout();
            this.mirrorGroupBox.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            this.groupBox1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.groupBox1.Controls.Add(this.Ztext);
            this.groupBox1.Controls.Add(this.Ytext);
            this.groupBox1.Controls.Add(this.Xtext);
            this.groupBox1.Controls.Add(this.PickPt);
            this.groupBox1.Controls.Add(this.PickLabelX);
            this.groupBox1.Controls.Add(this.PickLabelZ);
            this.groupBox1.Controls.Add(this.PickLabelY);
            this.groupBox1.Dock = System.Windows.Forms.DockStyle.Top;
            this.groupBox1.Location = new System.Drawing.Point(0, 0);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(256, 109);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Etabs Model Location";
            this.groupBox1.PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.groupBox1_PreviewKeyDown);
            // 
            // Ztext
            // 
            this.Ztext.Location = new System.Drawing.Point(32, 75);
            this.Ztext.Name = "Ztext";
            this.Ztext.Size = new System.Drawing.Size(100, 20);
            this.Ztext.TabIndex = 3;
            this.Ztext.Leave += new System.EventHandler(this.Ytext_Leave);
            // 
            // Ytext
            // 
            this.Ytext.Location = new System.Drawing.Point(32, 49);
            this.Ytext.Name = "Ytext";
            this.Ytext.Size = new System.Drawing.Size(100, 20);
            this.Ytext.TabIndex = 3;
            this.Ytext.Leave += new System.EventHandler(this.Ytext_Leave);
            // 
            // Xtext
            // 
            this.Xtext.Location = new System.Drawing.Point(33, 24);
            this.Xtext.Name = "Xtext";
            this.Xtext.Size = new System.Drawing.Size(100, 20);
            this.Xtext.TabIndex = 3;
            this.Xtext.Leave += new System.EventHandler(this.Ytext_Leave);
            // 
            // PickPt
            // 
            this.PickPt.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.PickPt.Location = new System.Drawing.Point(139, 41);
            this.PickPt.Name = "PickPt";
            this.PickPt.Size = new System.Drawing.Size(107, 34);
            this.PickPt.TabIndex = 1;
            this.PickPt.Text = "Pick Etabs Origin in RVT";
            this.PickPt.UseVisualStyleBackColor = true;
            this.PickPt.Click += new System.EventHandler(this.PickPt_Click);
            // 
            // PickLabelX
            // 
            this.PickLabelX.AutoSize = true;
            this.PickLabelX.Location = new System.Drawing.Point(9, 26);
            this.PickLabelX.Name = "PickLabelX";
            this.PickLabelX.Size = new System.Drawing.Size(17, 13);
            this.PickLabelX.TabIndex = 2;
            this.PickLabelX.Text = "X:";
            // 
            // PickLabelZ
            // 
            this.PickLabelZ.AutoSize = true;
            this.PickLabelZ.Location = new System.Drawing.Point(9, 78);
            this.PickLabelZ.Name = "PickLabelZ";
            this.PickLabelZ.Size = new System.Drawing.Size(17, 13);
            this.PickLabelZ.TabIndex = 2;
            this.PickLabelZ.Text = "Z:";
            // 
            // PickLabelY
            // 
            this.PickLabelY.AutoSize = true;
            this.PickLabelY.Location = new System.Drawing.Point(9, 52);
            this.PickLabelY.Name = "PickLabelY";
            this.PickLabelY.Size = new System.Drawing.Size(17, 13);
            this.PickLabelY.TabIndex = 2;
            this.PickLabelY.Text = "Y:";
            // 
            // AngleGroupBox
            // 
            this.AngleGroupBox.Controls.Add(this.angleText);
            this.AngleGroupBox.Controls.Add(this.label1);
            this.AngleGroupBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.AngleGroupBox.Location = new System.Drawing.Point(0, 109);
            this.AngleGroupBox.Name = "AngleGroupBox";
            this.AngleGroupBox.Size = new System.Drawing.Size(256, 63);
            this.AngleGroupBox.TabIndex = 0;
            this.AngleGroupBox.TabStop = false;
            this.AngleGroupBox.Text = "Etabs Model Angle";
            this.AngleGroupBox.PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.groupBox1_PreviewKeyDown);
            // 
            // angleText
            // 
            this.angleText.Location = new System.Drawing.Point(32, 23);
            this.angleText.Name = "angleText";
            this.angleText.Size = new System.Drawing.Size(100, 20);
            this.angleText.TabIndex = 5;
            this.angleText.Text = "0";
            this.angleText.EnabledChanged += new System.EventHandler(this.angleText_EnabledChanged);
            this.angleText.TextChanged += new System.EventHandler(this.textBox1_TextChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(6, 26);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(20, 13);
            this.label1.TabIndex = 4;
            this.label1.Text = "Φ:";
            // 
            // Okbut
            // 
            this.Okbut.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.Okbut.Location = new System.Drawing.Point(0, 301);
            this.Okbut.Name = "Okbut";
            this.Okbut.Size = new System.Drawing.Size(256, 25);
            this.Okbut.TabIndex = 1;
            this.Okbut.Text = "Ok";
            this.Okbut.UseVisualStyleBackColor = true;
            this.Okbut.Click += new System.EventHandler(this.Okbut_Click);
            // 
            // mirrorGroupBox
            // 
            this.mirrorGroupBox.Controls.Add(this.mirroredCheckY);
            this.mirrorGroupBox.Controls.Add(this.mirroredCheckX);
            this.mirrorGroupBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.mirrorGroupBox.Location = new System.Drawing.Point(0, 172);
            this.mirrorGroupBox.Name = "mirrorGroupBox";
            this.mirrorGroupBox.Size = new System.Drawing.Size(256, 69);
            this.mirrorGroupBox.TabIndex = 1;
            this.mirrorGroupBox.TabStop = false;
            this.mirrorGroupBox.Text = "Mirror";
            this.mirrorGroupBox.PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.groupBox1_PreviewKeyDown);
            // 
            // mirroredCheckY
            // 
            this.mirroredCheckY.AutoSize = true;
            this.mirroredCheckY.Location = new System.Drawing.Point(6, 43);
            this.mirroredCheckY.Name = "mirroredCheckY";
            this.mirroredCheckY.Size = new System.Drawing.Size(105, 17);
            this.mirroredCheckY.TabIndex = 0;
            this.mirroredCheckY.Text = "Mirrored About Y";
            this.mirroredCheckY.UseVisualStyleBackColor = true;
            this.mirroredCheckY.CheckedChanged += new System.EventHandler(this.mirroredCheckX_CheckedChanged);
            // 
            // mirroredCheckX
            // 
            this.mirroredCheckX.AutoSize = true;
            this.mirroredCheckX.Location = new System.Drawing.Point(7, 20);
            this.mirroredCheckX.Name = "mirroredCheckX";
            this.mirroredCheckX.Size = new System.Drawing.Size(105, 17);
            this.mirroredCheckX.TabIndex = 0;
            this.mirroredCheckX.Text = "Mirrored About X";
            this.mirroredCheckX.UseVisualStyleBackColor = true;
            this.mirroredCheckX.CheckedChanged += new System.EventHandler(this.mirroredCheckX_CheckedChanged);
            // 
            // CancelBut
            // 
            this.CancelBut.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBut.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.CancelBut.Location = new System.Drawing.Point(0, 276);
            this.CancelBut.Name = "CancelBut";
            this.CancelBut.Size = new System.Drawing.Size(256, 25);
            this.CancelBut.TabIndex = 1;
            this.CancelBut.Text = "Cancel";
            this.CancelBut.UseVisualStyleBackColor = true;
            this.CancelBut.Click += new System.EventHandler(this.CancelBut_Click);
            // 
            // TranslationForm
            // 
            this.AcceptButton = this.Okbut;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBut;
            this.ClientSize = new System.Drawing.Size(256, 326);
            this.ControlBox = false;
            this.Controls.Add(this.CancelBut);
            this.Controls.Add(this.Okbut);
            this.Controls.Add(this.mirrorGroupBox);
            this.Controls.Add(this.AngleGroupBox);
            this.Controls.Add(this.groupBox1);
            this.KeyPreview = true;
            this.MinimumSize = new System.Drawing.Size(272, 342);
            this.Name = "TranslationForm";
            this.Text = "TranslationForm";
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.TranslationForm_KeyPress);
            this.PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.groupBox1_PreviewKeyDown);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.AngleGroupBox.ResumeLayout(false);
            this.AngleGroupBox.PerformLayout();
            this.mirrorGroupBox.ResumeLayout(false);
            this.mirrorGroupBox.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Label PickLabelZ;
        private System.Windows.Forms.Label PickLabelY;
        private System.Windows.Forms.Label PickLabelX;
        private System.Windows.Forms.Button PickPt;
        private System.Windows.Forms.GroupBox AngleGroupBox;
        private System.Windows.Forms.TextBox Ztext;
        private System.Windows.Forms.TextBox Ytext;
        private System.Windows.Forms.TextBox Xtext;
        private System.Windows.Forms.Button Okbut;
        private System.Windows.Forms.GroupBox mirrorGroupBox;
        private System.Windows.Forms.TextBox angleText;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button CancelBut;
        private System.Windows.Forms.CheckBox mirroredCheckY;
        private System.Windows.Forms.CheckBox mirroredCheckX;
    }
}